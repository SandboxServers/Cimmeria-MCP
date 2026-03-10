using System.Text.Json;
using CimmeriaMcp.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace CimmeriaMcp.Functions.Functions;

public class MetricsEndpoint
{
    private readonly MetricsService _metricsService;

    public MetricsEndpoint(MetricsService metricsService)
    {
        _metricsService = metricsService;
    }

    [Function("GetMetrics")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "metrics")]
        HttpRequestData req)
    {
        var metrics = await _metricsService.GetMetricsAsync();

        var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Cache-Control", "public, max-age=60");

        await response.WriteStringAsync(JsonSerializer.Serialize(metrics, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        }));

        return response;
    }
}
