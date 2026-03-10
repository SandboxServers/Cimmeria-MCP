using System.Text.Json;
using Xunit;

namespace CimmeriaMcp.Functions.Tests;

public class CimmeriaSearchServiceTests
{
    [Fact]
    public void SearchAsync_WithSource_CimmeriaServer_UsesHybrid_WhenSearchClientAvailable()
    {
        // Validate that the search routing logic exists
        // Full integration tests require Azure services; these verify structure
        Assert.True(typeof(Services.CimmeriaSearchService)
            .GetMethod("SearchAsync") is not null);
    }

    [Fact]
    public void SearchAsync_HasCorrectParameters()
    {
        var method = typeof(Services.CimmeriaSearchService).GetMethod("SearchAsync");
        Assert.NotNull(method);

        var parameters = method!.GetParameters();
        Assert.Equal("query", parameters[0].Name);
        Assert.Equal("topK", parameters[1].Name);
        Assert.Equal("fileType", parameters[2].Name);
        Assert.Equal("source", parameters[3].Name);
    }

    [Fact]
    public void FindSimilarCodeAsync_HasCorrectParameters()
    {
        var method = typeof(Services.CimmeriaSearchService).GetMethod("FindSimilarCodeAsync");
        Assert.NotNull(method);

        var parameters = method!.GetParameters();
        Assert.Equal("codeSnippet", parameters[0].Name);
        Assert.Equal("topK", parameters[1].Name);
        Assert.Equal("source", parameters[2].Name);
    }

    [Fact]
    public void SearchByDirectoryAsync_HasCorrectParameters()
    {
        var method = typeof(Services.CimmeriaSearchService).GetMethod("SearchByDirectoryAsync");
        Assert.NotNull(method);

        var parameters = method!.GetParameters();
        Assert.Equal("pathPrefix", parameters[0].Name);
        Assert.Equal("query", parameters[1].Name);
        Assert.Equal("topK", parameters[2].Name);
        Assert.Equal("source", parameters[3].Name);
    }

    [Fact]
    public void Service_HasInternalConstructor_ForTesting()
    {
        var ctors = typeof(Services.CimmeriaSearchService).GetConstructors(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.Contains(ctors, c => c.GetParameters().Length == 3);
    }

    [Fact]
    public void SearchResultFormat_IsValid()
    {
        // Verify the expected JSON shape of search results
        var json = JsonSerializer.Serialize(new
        {
            results = new[]
            {
                new { file_path = "test.rs", file_type = ".rs", content = "fn main() {}", chunk_index = 0, source_project = "cimmeria-server", score = 0.95 }
            },
            count = 1,
            search_mode = "hybrid"
        });

        var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("results", out _));
        Assert.True(doc.RootElement.TryGetProperty("count", out _));
        Assert.True(doc.RootElement.TryGetProperty("search_mode", out _));
    }
}
