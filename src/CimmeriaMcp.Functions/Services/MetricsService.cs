using Azure.Identity;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;

namespace CimmeriaMcp.Functions.Services;

public class MetricsService
{
    internal const int CacheDurationSeconds = 60;

    private readonly MetricsQueryClient _metricsClient;
    private readonly string? _appInsightsResourceId;
    private readonly string? _cosmosResourceId;
    private readonly string? _searchResourceId;

    private object? _cachedMetrics;
    private DateTimeOffset _cacheExpiry = DateTimeOffset.MinValue;

    public MetricsService()
    {
        _metricsClient = new MetricsQueryClient(new DefaultAzureCredential());
        _appInsightsResourceId = Environment.GetEnvironmentVariable("APPINSIGHTS_RESOURCE_ID");
        _cosmosResourceId = Environment.GetEnvironmentVariable("COSMOS_RESOURCE_ID");
        _searchResourceId = Environment.GetEnvironmentVariable("SEARCH_RESOURCE_ID");
    }

    internal MetricsService(MetricsQueryClient metricsClient)
    {
        _metricsClient = metricsClient;
    }

    public async Task<object> GetMetricsAsync()
    {
        if (_cachedMetrics is not null && DateTimeOffset.UtcNow < _cacheExpiry)
            return _cachedMetrics;

        var appInsights = await QueryAppInsightsAsync();
        var cosmos = await QueryCosmosAsync();
        var search = await QuerySearchAsync();

        _cachedMetrics = new
        {
            appInsights,
            cosmos,
            search,
            timestamp = DateTimeOffset.UtcNow.ToString("o")
        };
        _cacheExpiry = DateTimeOffset.UtcNow.AddSeconds(CacheDurationSeconds);

        return _cachedMetrics;
    }

    private async Task<object> QueryAppInsightsAsync()
    {
        if (string.IsNullOrEmpty(_appInsightsResourceId))
            return new { available = false };

        try
        {
            var response = await _metricsClient.QueryResourceAsync(
                _appInsightsResourceId,
                ["requests/count", "requests/duration", "requests/failed"],
                new MetricsQueryOptions
                {
                    TimeRange = new QueryTimeRange(TimeSpan.FromHours(24)),
                    Granularity = TimeSpan.FromHours(1)
                });

            var requestCount = GetMetricValues(response.Value, "requests/count");
            var avgDuration = GetMetricValues(response.Value, "requests/duration");
            var failedCount = GetMetricValues(response.Value, "requests/failed");

            return new
            {
                available = true,
                requestCount,
                avgDuration,
                failedCount
            };
        }
        catch
        {
            return new { available = false };
        }
    }

    private async Task<object> QueryCosmosAsync()
    {
        if (string.IsNullOrEmpty(_cosmosResourceId))
            return new { available = false };

        try
        {
            var response = await _metricsClient.QueryResourceAsync(
                _cosmosResourceId,
                ["NormalizedRUConsumption", "TotalRequests"],
                new MetricsQueryOptions
                {
                    TimeRange = new QueryTimeRange(TimeSpan.FromHours(24)),
                    Granularity = TimeSpan.FromHours(1)
                });

            var ruConsumption = GetMetricValues(response.Value, "NormalizedRUConsumption");
            var totalRequests = GetMetricValues(response.Value, "TotalRequests");

            return new
            {
                available = true,
                ruConsumption,
                totalRequests
            };
        }
        catch
        {
            return new { available = false };
        }
    }

    private async Task<object> QuerySearchAsync()
    {
        if (string.IsNullOrEmpty(_searchResourceId))
            return new { available = false };

        try
        {
            var response = await _metricsClient.QueryResourceAsync(
                _searchResourceId,
                ["SearchQueriesPerSecond", "SearchLatency"],
                new MetricsQueryOptions
                {
                    TimeRange = new QueryTimeRange(TimeSpan.FromHours(24)),
                    Granularity = TimeSpan.FromHours(1)
                });

            var qps = GetMetricValues(response.Value, "SearchQueriesPerSecond");
            var latency = GetMetricValues(response.Value, "SearchLatency");

            return new
            {
                available = true,
                qps,
                latency
            };
        }
        catch
        {
            return new { available = false };
        }
    }

    private static List<object> GetMetricValues(MetricsQueryResult result, string metricName)
    {
        var values = new List<object>();
        var metric = result.Metrics.FirstOrDefault(m => m.Name == metricName);
        if (metric is null) return values;

        var timeSeries = metric.TimeSeries.FirstOrDefault();
        if (timeSeries is null) return values;

        foreach (var point in timeSeries.Values)
        {
            values.Add(new
            {
                time = point.TimeStamp.ToString("o"),
                value = point.Average ?? point.Total ?? point.Count ?? 0
            });
        }

        return values;
    }
}
