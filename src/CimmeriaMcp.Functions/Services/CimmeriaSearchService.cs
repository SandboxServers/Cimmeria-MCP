using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Microsoft.Azure.Cosmos;
using OpenAI.Embeddings;

namespace CimmeriaMcp.Functions.Services;

public class CimmeriaSearchService
{
    private const string DatabaseName = "cimmeria";
    private const string ContainerName = "code-chunks";
    private const string EmbeddingModel = "text-embedding-3-small";

    private readonly Container _container;
    private readonly EmbeddingClient _embeddingClient;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public CimmeriaSearchService()
    {
        var cosmosEndpoint = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT")
            ?? throw new InvalidOperationException("COSMOS_ENDPOINT is not configured.");
        var cosmosKey = Environment.GetEnvironmentVariable("COSMOS_KEY")
            ?? throw new InvalidOperationException("COSMOS_KEY is not configured.");
        var openAiEndpoint = Environment.GetEnvironmentVariable("OPENAI_ENDPOINT")
            ?? throw new InvalidOperationException("OPENAI_ENDPOINT is not configured.");
        var openAiKey = Environment.GetEnvironmentVariable("OPENAI_KEY")
            ?? throw new InvalidOperationException("OPENAI_KEY is not configured.");

        var cosmosClient = new CosmosClient(cosmosEndpoint, cosmosKey, new CosmosClientOptions
        {
            SerializerOptions = new CosmosSerializationOptions
            {
                PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
            }
        });
        _container = cosmosClient.GetContainer(DatabaseName, ContainerName);

        var azureOpenAiClient = new AzureOpenAIClient(
            new Uri(openAiEndpoint),
            new AzureKeyCredential(openAiKey));
        _embeddingClient = azureOpenAiClient.GetEmbeddingClient(EmbeddingModel);
    }

    private async Task<float[]> GetEmbeddingAsync(string text)
    {
        var options = new EmbeddingGenerationOptions { Dimensions = 505 };
        var response = await _embeddingClient.GenerateEmbeddingAsync(text, options);
        return response.Value.ToFloats().ToArray();
    }

    public async Task<string> SearchAsync(string query, int topK = 8, string? fileType = null, string? source = null)
    {
        var embedding = await GetEmbeddingAsync(query);

        var filters = new List<string>();
        if (!string.IsNullOrEmpty(fileType))
            filters.Add($"c.file_type = '{fileType}'");
        if (!string.IsNullOrEmpty(source))
            filters.Add($"c.source_project = '{source}'");

        var whereClause = filters.Count > 0 ? "WHERE " + string.Join(" AND ", filters) : "";

        var sql = $"SELECT TOP {topK} c.file_path, c.file_type, c.content, c.chunk_index, c.source_project, " +
                  $"VectorDistance(c.embedding, @embedding) AS distance " +
                  $"FROM c {whereClause} " +
                  $"ORDER BY VectorDistance(c.embedding, @embedding)";

        var queryDef = new QueryDefinition(sql)
            .WithParameter("@embedding", embedding);

        var items = new List<object>();
        using var iterator = _container.GetItemQueryIterator<dynamic>(queryDef);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            foreach (var item in response)
            {
                items.Add(new
                {
                    file_path = (string)item.file_path,
                    file_type = (string)item.file_type,
                    content = (string)item.content,
                    chunk_index = (int)item.chunk_index,
                    source_project = (string)item.source_project,
                    distance = (double)item.distance
                });
            }
        }

        return JsonSerializer.Serialize(new { results = items, count = items.Count }, JsonOptions);
    }

    public async Task<string> ListFilesAsync(string? fileType = null, string? source = null)
    {
        var filters = new List<string>();
        if (!string.IsNullOrEmpty(fileType))
            filters.Add($"c.file_type = '{fileType}'");
        if (!string.IsNullOrEmpty(source))
            filters.Add($"c.source_project = '{source}'");

        var whereClause = filters.Count > 0 ? "WHERE " + string.Join(" AND ", filters) : "";
        var sql = $"SELECT DISTINCT VALUE c.file_path FROM c {whereClause} ORDER BY c.file_path";

        var filePaths = new List<string>();
        using var iterator = _container.GetItemQueryIterator<string>(new QueryDefinition(sql));
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            filePaths.AddRange(response);
        }

        return JsonSerializer.Serialize(new { files = filePaths, count = filePaths.Count }, JsonOptions);
    }

    public async Task<string> GetFileContentAsync(string filePath, string? source = null)
    {
        var filters = new List<string> { "c.file_path = @filePath" };
        if (!string.IsNullOrEmpty(source))
            filters.Add($"c.source_project = '{source}'");

        var whereClause = "WHERE " + string.Join(" AND ", filters);
        var sql = $"SELECT c.file_path, c.file_type, c.content, c.chunk_index, c.source_project " +
                  $"FROM c {whereClause} ORDER BY c.chunk_index";

        var queryDef = new QueryDefinition(sql)
            .WithParameter("@filePath", filePath);

        var contentParts = new List<string>();
        var fileType = "";
        var sourceProject = "";

        using var iterator = _container.GetItemQueryIterator<dynamic>(queryDef);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            foreach (var item in response)
            {
                contentParts.Add((string)item.content);
                fileType = (string)item.file_type;
                sourceProject = (string)item.source_project;
            }
        }

        if (contentParts.Count == 0)
            return JsonSerializer.Serialize(new { error = $"File not found: {filePath}" });

        return JsonSerializer.Serialize(new
        {
            file_path = filePath,
            file_type = fileType,
            source_project = sourceProject,
            total_chunks = contentParts.Count,
            full_content = string.Join("\n", contentParts)
        }, JsonOptions);
    }

    public async Task<string> FindSimilarCodeAsync(string codeSnippet, int topK = 5, string? source = null)
    {
        var embedding = await GetEmbeddingAsync(codeSnippet);

        var whereClause = !string.IsNullOrEmpty(source) ? $"WHERE c.source_project = '{source}'" : "";

        var sql = $"SELECT TOP {topK} c.file_path, c.file_type, c.content, c.chunk_index, c.source_project, " +
                  $"VectorDistance(c.embedding, @embedding) AS distance " +
                  $"FROM c {whereClause} " +
                  $"ORDER BY VectorDistance(c.embedding, @embedding)";

        var queryDef = new QueryDefinition(sql)
            .WithParameter("@embedding", embedding);

        var items = new List<object>();
        using var iterator = _container.GetItemQueryIterator<dynamic>(queryDef);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            foreach (var item in response)
            {
                items.Add(new
                {
                    file_path = (string)item.file_path,
                    file_type = (string)item.file_type,
                    content = (string)item.content,
                    chunk_index = (int)item.chunk_index,
                    source_project = (string)item.source_project,
                    distance = (double)item.distance
                });
            }
        }

        return JsonSerializer.Serialize(new { similar = items, count = items.Count }, JsonOptions);
    }

    public async Task<string> GetProjectOverviewAsync(string? source = null)
    {
        var whereClause = !string.IsNullOrEmpty(source) ? $"WHERE c.source_project = '{source}'" : "";

        // Total chunks
        var countSql = $"SELECT VALUE COUNT(1) FROM c {whereClause}";
        long totalChunks = 0;
        using (var iter = _container.GetItemQueryIterator<long>(new QueryDefinition(countSql)))
        {
            while (iter.HasMoreResults)
            {
                var resp = await iter.ReadNextAsync();
                foreach (var count in resp)
                    totalChunks += count;
            }
        }

        // File type counts (count distinct files per type)
        var typeSql = $"SELECT c.file_type, COUNT(1) AS chunk_count FROM c {whereClause} GROUP BY c.file_type";
        var fileTypes = new Dictionary<string, long>();
        using (var iter = _container.GetItemQueryIterator<dynamic>(new QueryDefinition(typeSql)))
        {
            while (iter.HasMoreResults)
            {
                var resp = await iter.ReadNextAsync();
                foreach (var item in resp)
                    fileTypes[(string)item.file_type] = (long)item.chunk_count;
            }
        }

        // Distinct directories
        var pathSql = $"SELECT DISTINCT VALUE c.file_path FROM c {whereClause}";
        var directories = new SortedSet<string>();
        using (var iter = _container.GetItemQueryIterator<string>(new QueryDefinition(pathSql)))
        {
            while (iter.HasMoreResults)
            {
                var resp = await iter.ReadNextAsync();
                foreach (var path in resp)
                {
                    var dir = Path.GetDirectoryName(path)?.Replace('\\', '/');
                    if (!string.IsNullOrEmpty(dir))
                        directories.Add(dir);
                }
            }
        }

        // Source projects
        var sourcesSql = "SELECT DISTINCT VALUE c.source_project FROM c";
        var sources = new List<string>();
        using (var iter = _container.GetItemQueryIterator<string>(new QueryDefinition(sourcesSql)))
        {
            while (iter.HasMoreResults)
            {
                var resp = await iter.ReadNextAsync();
                sources.AddRange(resp);
            }
        }

        return JsonSerializer.Serialize(new
        {
            total_chunks = totalChunks,
            file_types = fileTypes,
            directories = directories,
            directory_count = directories.Count,
            source_projects = sources
        }, JsonOptions);
    }

    public async Task<string> SearchByDirectoryAsync(string pathPrefix, string query, int topK = 8, string? source = null)
    {
        var embedding = await GetEmbeddingAsync(query);

        var filters = new List<string> { "STARTSWITH(c.file_path, @prefix)" };
        if (!string.IsNullOrEmpty(source))
            filters.Add($"c.source_project = '{source}'");

        var whereClause = "WHERE " + string.Join(" AND ", filters);

        var sql = $"SELECT TOP {topK} c.file_path, c.file_type, c.content, c.chunk_index, c.source_project, " +
                  $"VectorDistance(c.embedding, @embedding) AS distance " +
                  $"FROM c {whereClause} " +
                  $"ORDER BY VectorDistance(c.embedding, @embedding)";

        var queryDef = new QueryDefinition(sql)
            .WithParameter("@embedding", embedding)
            .WithParameter("@prefix", pathPrefix);

        var items = new List<object>();
        using var iterator = _container.GetItemQueryIterator<dynamic>(queryDef);
        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            foreach (var item in response)
            {
                items.Add(new
                {
                    file_path = (string)item.file_path,
                    file_type = (string)item.file_type,
                    content = (string)item.content,
                    chunk_index = (int)item.chunk_index,
                    source_project = (string)item.source_project,
                    distance = (double)item.distance
                });
            }
        }

        return JsonSerializer.Serialize(new { results = items, count = items.Count, directory = pathPrefix }, JsonOptions);
    }
}
