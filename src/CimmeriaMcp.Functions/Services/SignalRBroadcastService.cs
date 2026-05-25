using System.Diagnostics;
using System.Text.Json;
using CimmeriaMcp.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace CimmeriaMcp.Services;

/// <summary>
/// Wraps every MCP tool invocation in:
///   - a stopwatch for duration reporting,
///   - exception trapping (so the LLM caller sees structured JSON
///     errors instead of empty responses),
///   - a SignalR broadcast over the in-process <see cref="CimmeriaHub"/>
///     so any connected dashboard sees the event live.
///
/// Replaces the Azure-SignalR-Service variant. The shape of the
/// broadcast payload is preserved (`tool`, `durationMs`, `status`,
/// `timestamp`) so downstream consumers don't need updating.
/// </summary>
public sealed class SignalRBroadcastService
{
    private readonly IHubContext<CimmeriaHub> _hub;
    private readonly ILogger<SignalRBroadcastService> _log;

    public SignalRBroadcastService(IHubContext<CimmeriaHub> hub, ILogger<SignalRBroadcastService> log)
    {
        _hub = hub;
        _log = log;
    }

    public async Task<string> TrackToolAsync(string toolName, Func<Task<string>> work)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await work();
            sw.Stop();
            _ = BroadcastSafelyAsync(toolName, sw.ElapsedMilliseconds, "success");
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _ = BroadcastSafelyAsync(toolName, sw.ElapsedMilliseconds, "error");

            // Return error details as a JSON envelope rather than
            // rethrowing — the MCP dispatcher wraps Task<string>
            // returns directly into the tool-result `content` block,
            // and the JSON-RPC layer expects a string here. Throwing
            // would surface as JSON-RPC -32603 with no structure for
            // the LLM to chew on.
            var inner = ex.InnerException;
            _log.LogWarning(ex, "Tool '{Tool}' failed after {DurationMs}ms", toolName, sw.ElapsedMilliseconds);
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

    private async Task BroadcastSafelyAsync(string toolName, long durationMs, string status)
    {
        // SignalR client failures must never cascade into tool-call
        // failures. The broadcast is fire-and-forget; we only log if
        // it errors so the dashboard's silence is at least
        // observable.
        try
        {
            await _hub.Clients.All.SendAsync("toolInvocation", new
            {
                tool = toolName,
                durationMs,
                status,
                timestamp = DateTimeOffset.UtcNow.ToString("o"),
            });
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "SignalR broadcast failed for tool '{Tool}'", toolName);
        }
    }
}
