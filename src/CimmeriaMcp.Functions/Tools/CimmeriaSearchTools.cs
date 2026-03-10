using CimmeriaMcp.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;

namespace CimmeriaMcp.Functions.Tools;

public class CimmeriaSearchTools
{
    private readonly CimmeriaSearchService _searchService;

    public CimmeriaSearchTools(CimmeriaSearchService searchService)
    {
        _searchService = searchService;
    }

    [Function(nameof(SearchCimmeria))]
    public async Task<string> SearchCimmeria(
        [McpToolTrigger("search_cimmeria",
            "Semantic search across the Cimmeria server, SGW client, and BigWorld engine codebases. Returns code, docs, configs, and UI scripts.")]
        ToolInvocationContext context,
        [McpToolProperty("query", "Natural language query", isRequired: true)] string query,
        [McpToolProperty("top_k", "Number of results (1-20, default 8)")] int? topK,
        [McpToolProperty("file_type", "Filter by extension e.g. cpp, rs, lua, layout")] string? fileType,
        [McpToolProperty("source", "Filter by source: cimmeria-server, sgw-client, or bigworld-engine")] string? source)
    {
        var k = Math.Clamp(topK ?? 8, 1, 20);
        return await _searchService.SearchAsync(query, k, fileType, source);
    }

    [Function(nameof(ListCimmeriaFiles))]
    public async Task<string> ListCimmeriaFiles(
        [McpToolTrigger("list_cimmeria_files",
            "List all indexed files from the Cimmeria server, SGW client, and BigWorld engine codebases.")]
        ToolInvocationContext context,
        [McpToolProperty("file_type", "Filter by extension")] string? fileType,
        [McpToolProperty("source", "Filter by source: cimmeria-server, sgw-client, or bigworld-engine")] string? source)
    {
        return await _searchService.ListFilesAsync(fileType, source);
    }

    [Function(nameof(GetFileContent))]
    public async Task<string> GetFileContent(
        [McpToolTrigger("get_file_content",
            "Get full content of a specific file by path.")]
        ToolInvocationContext context,
        [McpToolProperty("file_path", "Relative path e.g. src/network/packet.cpp", isRequired: true)] string filePath,
        [McpToolProperty("source", "Source: cimmeria-server, sgw-client, or bigworld-engine (helps disambiguate)")] string? source)
    {
        return await _searchService.GetFileContentAsync(filePath, source);
    }

    [Function(nameof(FindSimilarCode))]
    public async Task<string> FindSimilarCode(
        [McpToolTrigger("find_similar_code",
            "Find code similar to a given snippet across all indexed codebases.")]
        ToolInvocationContext context,
        [McpToolProperty("code_snippet", "Code to find similar patterns for", isRequired: true)] string codeSnippet,
        [McpToolProperty("top_k", "Number of results (default 5)")] int? topK,
        [McpToolProperty("source", "Filter by source: cimmeria-server, sgw-client, or bigworld-engine")] string? source)
    {
        var k = Math.Clamp(topK ?? 5, 1, 20);
        return await _searchService.FindSimilarCodeAsync(codeSnippet, k, source);
    }

    [Function(nameof(GetProjectOverview))]
    public async Task<string> GetProjectOverview(
        [McpToolTrigger("get_project_overview",
            "Get file counts by type, directory tree, index stats, and available source projects.")]
        ToolInvocationContext context,
        [McpToolProperty("source", "Filter by source: cimmeria-server, sgw-client, or bigworld-engine")] string? source)
    {
        return await _searchService.GetProjectOverviewAsync(source);
    }

    [Function(nameof(SearchByDirectory))]
    public async Task<string> SearchByDirectory(
        [McpToolTrigger("search_by_directory",
            "Semantic search scoped to a specific directory path prefix.")]
        ToolInvocationContext context,
        [McpToolProperty("path_prefix", "Directory prefix e.g. src/network/", isRequired: true)] string pathPrefix,
        [McpToolProperty("query", "Natural language query", isRequired: true)] string query,
        [McpToolProperty("top_k", "Number of results (default 8)")] int? topK,
        [McpToolProperty("source", "Filter by source: cimmeria-server, sgw-client, or bigworld-engine")] string? source)
    {
        var k = Math.Clamp(topK ?? 8, 1, 20);
        return await _searchService.SearchByDirectoryAsync(pathPrefix, query, k, source);
    }
}
