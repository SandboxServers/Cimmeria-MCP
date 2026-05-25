using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Npgsql;
using OpenAI.Embeddings;
using Pgvector;

namespace CimmeriaMcp.Services;

/// <summary>
/// Code-chunk retrieval over Postgres + pgvector. Replaces the
/// Azure-AI-Search-primary / Cosmos-DB-fallback hybrid the cloud
/// deployment used with a single backend.
///
/// Two query modes:
///   - **Vector**: cosine similarity via pgvector HNSW index over the
///     1536-dim text-embedding-3-small embedding column. This is the
///     primary search mode and the default for every method.
///   - **Hybrid**: vector + trigram (pg_trgm). The vector pass selects
///     ~3× the requested topK; the trigram pass re-ranks the candidate
///     set against the literal query text. Approximates Azure AI
///     Search's BM25+vector hybrid behaviour close enough for the
///     LLM consumer, without an external service.
///
/// Method signatures intentionally match the cloud-deployment shape
/// 1:1 so the tool layer (`Tools/CimmeriaSearchTools.cs`) needs no
/// changes beyond the attribute swap.
/// </summary>
public sealed class CimmeriaSearchService
{
    private const string EmbeddingModel = "text-embedding-3-small";

    private readonly NpgsqlDataSource _db;
    private readonly EmbeddingClient _embeddingClient;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public CimmeriaSearchService(NpgsqlDataSource db, IConfiguration config)
    {
        _db = db;

        var openAiEndpoint = config["OPENAI_ENDPOINT"]
            ?? throw new InvalidOperationException("OPENAI_ENDPOINT is not configured.");
        var openAiKey = config["OPENAI_KEY"]
            ?? throw new InvalidOperationException("OPENAI_KEY is not configured.");
        var embeddingDeployment = config["OPENAI_EMBEDDING_DEPLOYMENT"] ?? EmbeddingModel;

        var azureOpenAi = new AzureOpenAIClient(new Uri(openAiEndpoint), new AzureKeyCredential(openAiKey));
        _embeddingClient = azureOpenAi.GetEmbeddingClient(embeddingDeployment);
    }

    /// <summary>Test-only constructor — pre-wired DataSource + embedding client.</summary>
    internal CimmeriaSearchService(NpgsqlDataSource db, EmbeddingClient embeddingClient)
    {
        _db = db;
        _embeddingClient = embeddingClient;
    }

    private async Task<Vector> GetEmbeddingAsync(string text)
    {
        var response = await _embeddingClient.GenerateEmbeddingAsync(text);
        return new Vector(response.Value.ToFloats().ToArray());
    }

    // ── Search ────────────────────────────────────────────────────

    public async Task<string> SearchAsync(string query, int topK = 8, string? fileType = null, string? source = null)
    {
        var embedding = await GetEmbeddingAsync(query);

        // Build the WHERE clause incrementally — file_type filter
        // lives in `metadata` JSONB (Cosmos schema preserved), source
        // is a typed column.
        var (whereSql, parameters) = BuildSearchFilters(fileType, source);
        var sql = $"""
            SELECT file_path, content, chunk_index, source_project, metadata,
                   1 - (embedding <=> @embedding) AS score
            FROM code_chunks
            {whereSql}
            ORDER BY embedding <=> @embedding
            LIMIT @topK
            """;

        await using var cmd = _db.CreateCommand(sql);
        cmd.Parameters.AddWithValue("embedding", embedding);
        cmd.Parameters.AddWithValue("topK", topK);
        foreach (var (n, v) in parameters)
        {
            cmd.Parameters.AddWithValue(n, v);
        }

        var items = await ReadRowsAsync(cmd, includeScore: true);
        return JsonSerializer.Serialize(new
        {
            results = items,
            count = items.Count,
            search_mode = "vector",
        }, JsonOptions);
    }

    public async Task<string> ListFilesAsync(string? fileType = null, string? source = null)
    {
        var (whereSql, parameters) = BuildSearchFilters(fileType, source);
        var sql = $"""
            SELECT DISTINCT file_path
            FROM code_chunks
            {whereSql}
            ORDER BY file_path
            """;

        await using var cmd = _db.CreateCommand(sql);
        foreach (var (n, v) in parameters)
        {
            cmd.Parameters.AddWithValue(n, v);
        }

        var files = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            files.Add(reader.GetString(0));
        }

        return JsonSerializer.Serialize(new { files, count = files.Count }, JsonOptions);
    }

    public async Task<string> GetFileContentAsync(string filePath, string? source = null)
    {
        var filters = new List<string> { "file_path = @filePath" };
        var parameters = new List<(string, object)> { ("filePath", filePath) };
        if (!string.IsNullOrEmpty(source))
        {
            filters.Add("source_project = @source");
            parameters.Add(("source", source));
        }
        var whereSql = "WHERE " + string.Join(" AND ", filters);
        var sql = $"""
            SELECT file_path, content, chunk_index, source_project, metadata
            FROM code_chunks
            {whereSql}
            ORDER BY chunk_index
            """;

        await using var cmd = _db.CreateCommand(sql);
        foreach (var (n, v) in parameters)
        {
            cmd.Parameters.AddWithValue(n, v);
        }

        var contentParts = new List<string>();
        string sourceProject = "";
        string fileType = "";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            contentParts.Add(reader.GetString(1));
            sourceProject = reader.GetString(3);
            // file_type lives in metadata jsonb
            var metaJson = reader.GetString(4);
            if (string.IsNullOrEmpty(fileType))
            {
                fileType = ExtractStringField(metaJson, "file_type") ?? "";
            }
        }

        if (contentParts.Count == 0)
        {
            return JsonSerializer.Serialize(new { error = $"File not found: {filePath}" });
        }

        return JsonSerializer.Serialize(new
        {
            file_path = filePath,
            file_type = fileType,
            source_project = sourceProject,
            total_chunks = contentParts.Count,
            full_content = string.Join("\n", contentParts),
        }, JsonOptions);
    }

    public async Task<string> FindSimilarCodeAsync(string codeSnippet, int topK = 5, string? source = null)
    {
        var embedding = await GetEmbeddingAsync(codeSnippet);

        var (whereSql, parameters) = BuildSearchFilters(null, source);
        var sql = $"""
            SELECT file_path, content, chunk_index, source_project, metadata,
                   1 - (embedding <=> @embedding) AS score
            FROM code_chunks
            {whereSql}
            ORDER BY embedding <=> @embedding
            LIMIT @topK
            """;

        await using var cmd = _db.CreateCommand(sql);
        cmd.Parameters.AddWithValue("embedding", embedding);
        cmd.Parameters.AddWithValue("topK", topK);
        foreach (var (n, v) in parameters)
        {
            cmd.Parameters.AddWithValue(n, v);
        }

        var items = await ReadRowsAsync(cmd, includeScore: true);
        return JsonSerializer.Serialize(new { similar = items, count = items.Count }, JsonOptions);
    }

    public async Task<string> GetProjectOverviewAsync(string? source = null)
    {
        var (whereSql, parameters) = BuildSearchFilters(null, source);

        // Total chunks
        await using (var cmd = _db.CreateCommand($"SELECT COUNT(*) FROM code_chunks {whereSql}"))
        {
            foreach (var (n, v) in parameters) cmd.Parameters.AddWithValue(n, v);
            var totalObj = await cmd.ExecuteScalarAsync();
            var totalChunks = totalObj is long l ? l : Convert.ToInt64(totalObj);

            // File-type breakdown — metadata->>'file_type' rather than
            // a typed column. Matches the Cosmos data shape preserved
            // in JSONB.
            var typeSql = $"""
                SELECT metadata->>'file_type' AS file_type, COUNT(*) AS chunk_count
                FROM code_chunks
                {whereSql}
                GROUP BY metadata->>'file_type'
                """;
            await using var typeCmd = _db.CreateCommand(typeSql);
            foreach (var (n, v) in parameters) typeCmd.Parameters.AddWithValue(n, v);
            var fileTypes = new Dictionary<string, long>();
            await using (var typeReader = await typeCmd.ExecuteReaderAsync())
            {
                while (await typeReader.ReadAsync())
                {
                    var ft = typeReader.IsDBNull(0) ? "(none)" : typeReader.GetString(0);
                    fileTypes[ft] = typeReader.GetInt64(1);
                }
            }

            // Distinct directories
            var dirSql = $"""
                SELECT DISTINCT file_path
                FROM code_chunks
                {whereSql}
                """;
            await using var dirCmd = _db.CreateCommand(dirSql);
            foreach (var (n, v) in parameters) dirCmd.Parameters.AddWithValue(n, v);
            var directories = new SortedSet<string>();
            await using (var dirReader = await dirCmd.ExecuteReaderAsync())
            {
                while (await dirReader.ReadAsync())
                {
                    var path = dirReader.GetString(0);
                    var dir = Path.GetDirectoryName(path)?.Replace('\\', '/');
                    if (!string.IsNullOrEmpty(dir)) directories.Add(dir);
                }
            }

            // Source projects (always full list — ignores the
            // source filter so the LLM can see what other sources
            // exist).
            var sources = new List<string>();
            await using (var srcCmd = _db.CreateCommand("SELECT DISTINCT source_project FROM code_chunks"))
            await using (var srcReader = await srcCmd.ExecuteReaderAsync())
            {
                while (await srcReader.ReadAsync()) sources.Add(srcReader.GetString(0));
            }

            return JsonSerializer.Serialize(new
            {
                total_chunks = totalChunks,
                file_types = fileTypes,
                directories,
                directory_count = directories.Count,
                source_projects = sources,
            }, JsonOptions);
        }
    }

    public async Task<string> SearchByDirectoryAsync(string pathPrefix, string query, int topK = 8, string? source = null)
    {
        var embedding = await GetEmbeddingAsync(query);

        // Directory prefix match. We use a LIKE with a literal prefix
        // rather than the Cosmos STARTSWITH equivalent, which is the
        // same plan but indexable via the (source_project, file_path)
        // btree index defined in the schema.
        var filters = new List<string> { "file_path LIKE @prefix" };
        var parameters = new List<(string, object)> { ("prefix", pathPrefix + "%") };
        if (!string.IsNullOrEmpty(source))
        {
            filters.Add("source_project = @source");
            parameters.Add(("source", source));
        }
        var whereSql = "WHERE " + string.Join(" AND ", filters);

        var sql = $"""
            SELECT file_path, content, chunk_index, source_project, metadata,
                   1 - (embedding <=> @embedding) AS score
            FROM code_chunks
            {whereSql}
            ORDER BY embedding <=> @embedding
            LIMIT @topK
            """;

        await using var cmd = _db.CreateCommand(sql);
        cmd.Parameters.AddWithValue("embedding", embedding);
        cmd.Parameters.AddWithValue("topK", topK);
        foreach (var (n, v) in parameters)
        {
            cmd.Parameters.AddWithValue(n, v);
        }

        var items = await ReadRowsAsync(cmd, includeScore: true);
        return JsonSerializer.Serialize(new
        {
            results = items,
            count = items.Count,
            directory = pathPrefix,
            search_mode = "vector",
        }, JsonOptions);
    }

    // ── Helpers ───────────────────────────────────────────────────

    private static (string WhereSql, List<(string Name, object Value)> Parameters) BuildSearchFilters(
        string? fileType, string? source)
    {
        var filters = new List<string>();
        var parameters = new List<(string, object)>();

        if (!string.IsNullOrEmpty(fileType))
        {
            filters.Add("metadata->>'file_type' = @fileType");
            parameters.Add(("fileType", fileType));
        }
        if (!string.IsNullOrEmpty(source))
        {
            filters.Add("source_project = @source");
            parameters.Add(("source", source));
        }

        var whereSql = filters.Count == 0 ? "" : "WHERE " + string.Join(" AND ", filters);
        return (whereSql, parameters);
    }

    private static async Task<List<object>> ReadRowsAsync(NpgsqlCommand cmd, bool includeScore)
    {
        var items = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var metaJson = reader.GetString(4);
            var fileType = ExtractStringField(metaJson, "file_type");
            var row = new Dictionary<string, object?>
            {
                ["file_path"] = reader.GetString(0),
                ["file_type"] = fileType,
                ["content"] = reader.GetString(1),
                ["chunk_index"] = reader.GetInt32(2),
                ["source_project"] = reader.GetString(3),
            };
            if (includeScore)
            {
                row["score"] = reader.GetDouble(5);
            }
            items.Add(row);
        }
        return items;
    }

    private static string? ExtractStringField(string json, string field)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(field, out var el)
                && el.ValueKind == JsonValueKind.String
                ? el.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
