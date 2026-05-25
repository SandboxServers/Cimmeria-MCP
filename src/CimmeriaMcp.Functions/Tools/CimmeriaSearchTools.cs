using System.ComponentModel;
using CimmeriaMcp.Services;
using ModelContextProtocol.Server;

namespace CimmeriaMcp.Tools;

/// <summary>
/// RAG search MCP tools — semantic search across cimmeria-server,
/// sgw-client, and bigworld-engine codebases.
///
/// Discovery: the official MCP C# SDK reflects every class marked
/// with <see cref="McpServerToolTypeAttribute"/> and registers each
/// method marked <see cref="McpServerToolAttribute"/> as a tool.
/// Tool name comes from the method name converted to snake_case;
/// description comes from <see cref="DescriptionAttribute"/> on the
/// method; parameter descriptions come from <see cref="DescriptionAttribute"/>
/// on each parameter; required-ness comes from whether the C# parameter
/// has a default value (no default → required).
/// </summary>
[McpServerToolType]
public sealed class CimmeriaSearchTools
{
    private readonly CimmeriaSearchService _search;

    public CimmeriaSearchTools(CimmeriaSearchService search)
    {
        _search = search;
    }

    [McpServerTool(Name = "search_cimmeria")]
    [Description("Semantic search across the Cimmeria server, SGW client, and BigWorld engine codebases. Returns code, docs, configs, and UI scripts.")]
    public Task<string> SearchCimmeria(
        [Description("Natural language query")] string query,
        [Description("Number of results (1-20, default 8)")] int? topK = null,
        [Description("Filter by extension e.g. cpp, rs, lua, layout")] string? fileType = null,
        [Description("Filter by source: cimmeria-server, sgw-client, or bigworld-engine")] string? source = null)
    {
        var k = Math.Clamp(topK ?? 8, 1, 20);
        return _search.SearchAsync(query, k, fileType, source);
    }

    [McpServerTool(Name = "list_cimmeria_files")]
    [Description("List all indexed files from the Cimmeria server, SGW client, and BigWorld engine codebases.")]
    public Task<string> ListCimmeriaFiles(
        [Description("Filter by extension")] string? fileType = null,
        [Description("Filter by source: cimmeria-server, sgw-client, or bigworld-engine")] string? source = null)
        => _search.ListFilesAsync(fileType, source);

    [McpServerTool(Name = "get_file_content")]
    [Description("Get full content of a specific file by path.")]
    public Task<string> GetFileContent(
        [Description("Relative path e.g. src/network/packet.cpp")] string filePath,
        [Description("Source: cimmeria-server, sgw-client, or bigworld-engine (helps disambiguate)")] string? source = null)
        => _search.GetFileContentAsync(filePath, source);

    [McpServerTool(Name = "find_similar_code")]
    [Description("Find code similar to a given snippet across all indexed codebases.")]
    public Task<string> FindSimilarCode(
        [Description("Code to find similar patterns for")] string codeSnippet,
        [Description("Number of results (default 5)")] int? topK = null,
        [Description("Filter by source: cimmeria-server, sgw-client, or bigworld-engine")] string? source = null)
    {
        var k = Math.Clamp(topK ?? 5, 1, 20);
        return _search.FindSimilarCodeAsync(codeSnippet, k, source);
    }

    [McpServerTool(Name = "get_project_overview")]
    [Description("Get file counts by type, directory tree, index stats, and available source projects.")]
    public Task<string> GetProjectOverview(
        [Description("Filter by source: cimmeria-server, sgw-client, or bigworld-engine")] string? source = null)
        => _search.GetProjectOverviewAsync(source);

    [McpServerTool(Name = "search_by_directory")]
    [Description("Semantic search scoped to a specific directory path prefix.")]
    public Task<string> SearchByDirectory(
        [Description("Directory prefix e.g. src/network/")] string pathPrefix,
        [Description("Natural language query")] string query,
        [Description("Number of results (default 8)")] int? topK = null,
        [Description("Filter by source: cimmeria-server, sgw-client, or bigworld-engine")] string? source = null)
    {
        var k = Math.Clamp(topK ?? 8, 1, 20);
        return _search.SearchByDirectoryAsync(pathPrefix, query, k, source);
    }
}
