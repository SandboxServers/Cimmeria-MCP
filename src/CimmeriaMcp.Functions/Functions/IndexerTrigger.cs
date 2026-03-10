using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace CimmeriaMcp.Functions.Functions;

public class IndexerTrigger
{
    private static CancellationTokenSource? _debounceCts;
    private static readonly object _lock = new();
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(30);

    private readonly ILogger<IndexerTrigger> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public IndexerTrigger(ILogger<IndexerTrigger> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    [Function("IndexerTrigger")]
    public void Run(
        [CosmosDBTrigger(
            databaseName: "cimmeria",
            containerName: "code-chunks",
            Connection = "COSMOS_CONNECTION_STRING",
            LeaseContainerName = "leases",
            CreateLeaseContainerIfNotExists = false)]
        IReadOnlyList<object> changes)
    {
        if (changes is null || changes.Count == 0)
            return;

        _logger.LogInformation("Change feed received {Count} changes, debouncing indexer run", changes.Count);

        lock (_lock)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = new CancellationTokenSource();
            var token = _debounceCts.Token;

            _ = DebounceAndRunIndexerAsync(token);
        }
    }

    private async Task DebounceAndRunIndexerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(DebounceDelay, cancellationToken);
        }
        catch (TaskCanceledException)
        {
            return; // Debounce reset — another batch arrived
        }

        await RunIndexerAsync();
    }

    private async Task RunIndexerAsync()
    {
        var searchEndpoint = Environment.GetEnvironmentVariable("SEARCH_ENDPOINT");
        var searchKey = Environment.GetEnvironmentVariable("SEARCH_KEY");

        if (string.IsNullOrEmpty(searchEndpoint) || string.IsNullOrEmpty(searchKey))
        {
            _logger.LogWarning("SEARCH_ENDPOINT or SEARCH_KEY not configured, skipping indexer trigger");
            return;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            var url = $"{searchEndpoint}/indexers/cimmeria-cosmos-indexer/run?api-version=2024-07-01";

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("api-key", searchKey);

            var response = await client.SendAsync(request);

            if (response.IsSuccessStatusCode)
                _logger.LogInformation("AI Search indexer triggered successfully");
            else
                _logger.LogWarning("AI Search indexer trigger returned {StatusCode}", response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trigger AI Search indexer");
        }
    }
}
