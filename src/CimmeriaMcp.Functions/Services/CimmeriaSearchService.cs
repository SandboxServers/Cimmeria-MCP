using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Models;
using OpenAI.Embeddings;

namespace CimmeriaMcp.Functions.Services;

public class CimmeriaSearchService
{
    private const string IndexName = "cimmeria-code";
    private const string EmbeddingModel = "text-embedding-3-small";

    private readonly SearchClient _searchClient;
    private readonly EmbeddingClient _embeddingClient;

    public CimmeriaSearchService()
    {
        var searchEndpoint = Environment.GetEnvironmentVariable("SEARCH_ENDPOINT")
            ?? throw new InvalidOperationException("SEARCH_ENDPOINT is not configured.");
        var searchKey = Environment.GetEnvironmentVariable("SEARCH_KEY")
            ?? throw new InvalidOperationException("SEARCH_KEY is not configured.");
        var openAiEndpoint = Environment.GetEnvironmentVariable("OPENAI_ENDPOINT")
            ?? throw new InvalidOperationException("OPENAI_ENDPOINT is not configured.");
        var openAiKey = Environment.GetEnvironmentVariable("OPENAI_KEY")
            ?? throw new InvalidOperationException("OPENAI_KEY is not configured.");

        _searchClient = new SearchClient(
            new Uri(searchEndpoint),
            IndexName,
            new AzureKeyCredential(searchKey));

        var azureOpenAiClient = new AzureOpenAIClient(
            new Uri(openAiEndpoint),
            new AzureKeyCredential(openAiKey));

        _embeddingClient = azureOpenAiClient.GetEmbeddingClient(EmbeddingModel);
    }

    public async Task<ReadOnlyMemory<float>> GetEmbeddingAsync(string text)
    {
        var response = await _embeddingClient.GenerateEmbeddingAsync(text);
        return response.Value.ToFloats();
    }

    public async Task<string> SearchAsync(string query, int topK = 8, string? fileType = null)
    {
        var embedding = await GetEmbeddingAsync(query);

        var vectorQuery = new VectorizedQuery(embedding)
        {
            KNearestNeighborsCount = topK,
            Fields = { "embedding" }
        };

        var options = new SearchOptions
        {
            Size = topK,
            VectorSearch = new() { Queries = { vectorQuery } },
            Select = { "file_path", "file_type", "content", "chunk_index" },
            SearchMode = SearchMode.All
        };

        if (!string.IsNullOrEmpty(fileType))
        {
            options.Filter = $"file_type eq '{fileType}'";
        }

        // Hybrid search: text query + vector
        var results = await _searchClient.SearchAsync<SearchDocument>(query, options);

        var items = new List<object>();
        await foreach (var result in results.Value.GetResultsAsync())
        {
            items.Add(new
            {
                file_path = result.Document.GetString("file_path"),
                file_type = result.Document.GetString("file_type"),
                content = result.Document.GetString("content"),
                chunk_index = result.Document.GetInt32("chunk_index"),
                score = result.Score
            });
        }

        return JsonSerializer.Serialize(new { results = items, count = items.Count },
            new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task<string> ListFilesAsync(string? fileType = null)
    {
        var options = new SearchOptions
        {
            Size = 0,
            Facets = { "file_path,count:1000" }
        };

        if (!string.IsNullOrEmpty(fileType))
        {
            options.Filter = $"file_type eq '{fileType}'";
        }

        var results = await _searchClient.SearchAsync<SearchDocument>("*", options);

        var filePaths = new SortedSet<string>();
        await foreach (var result in results.Value.GetResultsAsync())
        {
            if (result.Document.TryGetValue("file_path", out var path))
            {
                filePaths.Add(path.ToString()!);
            }
        }

        return JsonSerializer.Serialize(new { files = filePaths, count = filePaths.Count },
            new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task<string> GetFileContentAsync(string filePath)
    {
        var options = new SearchOptions
        {
            Filter = $"file_path eq '{filePath}'",
            Size = 100,
            OrderBy = { "chunk_index asc" },
            Select = { "file_path", "file_type", "content", "chunk_index" }
        };

        var results = await _searchClient.SearchAsync<SearchDocument>("*", options);

        var chunks = new List<object>();
        var contentParts = new List<string>();

        await foreach (var result in results.Value.GetResultsAsync())
        {
            var content = result.Document.GetString("content");
            contentParts.Add(content);
            chunks.Add(new
            {
                chunk_index = result.Document.GetInt32("chunk_index"),
                content
            });
        }

        if (chunks.Count == 0)
        {
            return JsonSerializer.Serialize(new { error = $"File not found: {filePath}" });
        }

        return JsonSerializer.Serialize(new
        {
            file_path = filePath,
            total_chunks = chunks.Count,
            full_content = string.Join("\n", contentParts)
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task<string> FindSimilarCodeAsync(string codeSnippet, int topK = 5)
    {
        var embedding = await GetEmbeddingAsync(codeSnippet);

        var vectorQuery = new VectorizedQuery(embedding)
        {
            KNearestNeighborsCount = topK,
            Fields = { "embedding" }
        };

        var options = new SearchOptions
        {
            Size = topK,
            VectorSearch = new() { Queries = { vectorQuery } },
            Select = { "file_path", "file_type", "content", "chunk_index" }
        };

        var results = await _searchClient.SearchAsync<SearchDocument>(null, options);

        var items = new List<object>();
        await foreach (var result in results.Value.GetResultsAsync())
        {
            items.Add(new
            {
                file_path = result.Document.GetString("file_path"),
                file_type = result.Document.GetString("file_type"),
                content = result.Document.GetString("content"),
                chunk_index = result.Document.GetInt32("chunk_index"),
                score = result.Score
            });
        }

        return JsonSerializer.Serialize(new { similar = items, count = items.Count },
            new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task<string> GetProjectOverviewAsync()
    {
        var options = new SearchOptions
        {
            Size = 0,
            Facets = { "file_type,count:100", "file_path,count:1000" },
            IncludeTotalCount = true
        };

        var results = await _searchClient.SearchAsync<SearchDocument>("*", options);

        var fileTypes = new Dictionary<string, long>();
        if (results.Value.Facets.TryGetValue("file_type", out var typeFacets))
        {
            foreach (var facet in typeFacets)
            {
                fileTypes[facet.Value.ToString()!] = facet.Count ?? 0;
            }
        }

        var directories = new SortedSet<string>();
        if (results.Value.Facets.TryGetValue("file_path", out var pathFacets))
        {
            foreach (var facet in pathFacets)
            {
                var path = facet.Value.ToString()!;
                var dir = Path.GetDirectoryName(path)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(dir))
                {
                    directories.Add(dir);
                }
            }
        }

        return JsonSerializer.Serialize(new
        {
            total_chunks = results.Value.TotalCount,
            file_types = fileTypes,
            directories = directories,
            directory_count = directories.Count
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task<string> SearchByDirectoryAsync(string pathPrefix, string query, int topK = 8)
    {
        var embedding = await GetEmbeddingAsync(query);

        var vectorQuery = new VectorizedQuery(embedding)
        {
            KNearestNeighborsCount = topK,
            Fields = { "embedding" }
        };

        var options = new SearchOptions
        {
            Size = topK,
            VectorSearch = new() { Queries = { vectorQuery } },
            Filter = $"search.ismatch('{pathPrefix}*', 'file_path')",
            Select = { "file_path", "file_type", "content", "chunk_index" },
            SearchMode = SearchMode.All
        };

        var results = await _searchClient.SearchAsync<SearchDocument>(query, options);

        var items = new List<object>();
        await foreach (var result in results.Value.GetResultsAsync())
        {
            items.Add(new
            {
                file_path = result.Document.GetString("file_path"),
                file_type = result.Document.GetString("file_type"),
                content = result.Document.GetString("content"),
                chunk_index = result.Document.GetInt32("chunk_index"),
                score = result.Score
            });
        }

        return JsonSerializer.Serialize(new { results = items, count = items.Count, directory = pathPrefix },
            new JsonSerializerOptions { WriteIndented = true });
    }
}
