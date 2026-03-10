using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CimmeriaMcp.Functions.Services;

public class SignalRBroadcastService
{
    private const string HubName = "cimmeria";
    private readonly HttpClient _httpClient;
    private readonly string? _endpoint;
    private readonly string? _accessKey;

    public SignalRBroadcastService(IHttpClientFactory httpClientFactory)
    {
        _httpClient = httpClientFactory.CreateClient("SignalR");

        var connectionString = Environment.GetEnvironmentVariable("AzureSignalRConnectionString");
        if (string.IsNullOrEmpty(connectionString))
            return;

        // Parse "Endpoint=https://...;AccessKey=...;Version=1.0;"
        foreach (var part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;
            switch (kv[0].Trim())
            {
                case "Endpoint":
                    _endpoint = kv[1].TrimEnd('/');
                    break;
                case "AccessKey":
                    _accessKey = kv[1];
                    break;
            }
        }
    }

    /// <summary>
    /// Wraps a tool invocation, broadcasting a completion event to SignalR clients.
    /// </summary>
    public async Task<string> TrackToolAsync(string toolName, Func<Task<string>> work)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await work();
            sw.Stop();
            if (_endpoint is not null && _accessKey is not null)
                _ = BroadcastAsync(toolName, sw.ElapsedMilliseconds, "success");
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            if (_endpoint is not null && _accessKey is not null)
                _ = BroadcastAsync(toolName, sw.ElapsedMilliseconds, "error");

            // Return error details as JSON instead of rethrowing —
            // the MCP extension swallows exceptions into empty responses,
            // so the calling LLM never sees what went wrong.
            var inner = ex.InnerException;
            return JsonSerializer.Serialize(new
            {
                error = true,
                tool = toolName,
                message = ex.Message,
                exception_type = ex.GetType().Name,
                inner_message = inner?.Message,
                duration_ms = sw.ElapsedMilliseconds,
            });
        }
    }

    private async Task BroadcastAsync(string toolName, long durationMs, string status)
    {
        try
        {
            // Azure SignalR REST API: POST /api/v1/hubs/{hub}
            var url = $"{_endpoint}/api/v1/hubs/{HubName}";
            var token = GenerateAccessToken(url, TimeSpan.FromMinutes(5));

            var payload = new
            {
                target = "toolInvocation",
                arguments = new object[]
                {
                    new
                    {
                        tool = toolName,
                        durationMs,
                        status,
                        timestamp = DateTimeOffset.UtcNow.ToString("o")
                    }
                }
            };

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            await _httpClient.SendAsync(request);
        }
        catch
        {
            // Don't let SignalR failures break tool invocations
        }
    }

    private string GenerateAccessToken(string audience, TimeSpan lifetime)
    {
        var expiry = DateTimeOffset.UtcNow.Add(lifetime).ToUnixTimeSeconds();

        var header = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { alg = "HS256", typ = "JWT" })))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var claims = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { aud = audience, exp = expiry })))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var input = $"{header}.{claims}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_accessKey!));
        var sig = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(input)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{input}.{sig}";
    }
}
