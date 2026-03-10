using System.Reflection;
using Xunit;

namespace CimmeriaMcp.Functions.Tests;

public class CimmeriaSummarizationServiceTests
{
    [Fact]
    public void ChatModel_UsesDeploymentName_WithHyphens()
    {
        var field = typeof(Services.CimmeriaSummarizationService)
            .GetField("ChatModel", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        Assert.Equal("gpt-5-1-chat", field!.GetValue(null));
    }

    [Fact]
    public void CodexModel_UsesGpt51CodexMini()
    {
        var field = typeof(Services.CimmeriaSummarizationService)
            .GetField("CodexModel", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        Assert.Equal("gpt-5-1-codex-mini", field!.GetValue(null));
    }

    [Fact]
    public void EmbeddingModel_IsTextEmbedding3Small()
    {
        var field = typeof(Services.CimmeriaSummarizationService)
            .GetField("EmbeddingModel", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        Assert.Equal("text-embedding-3-small", field!.GetValue(null));
    }

    [Fact]
    public void ResponseFormatInstruction_ContainsRequiredSections()
    {
        var field = typeof(Services.CimmeriaSummarizationService)
            .GetField("ResponseFormatInstruction", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);

        var instruction = (string)field!.GetValue(null)!;
        Assert.Contains("Summary", instruction);
        Assert.Contains("Details", instruction);
        Assert.Contains("Sources", instruction);
        Assert.Contains("Confidence", instruction);
    }

    [Fact]
    public void Service_Has14AiSkillMethods()
    {
        var expectedMethods = new[]
        {
            "ExplainAsync", "GenerateEntityStubAsync", "TranslatePythonToRustAsync",
            "GenerateTestsAsync", "TroubleshootAsync", "ReviewCodeAsync",
            "CheckCompatibilityAsync", "AnalyzeImpactAsync", "PlanImplementationAsync",
            "SuggestNextAsync", "AnalyzeProtocolAsync", "TraceSequenceAsync",
            "GenerateDiagramAsync", "DecodeGameDesignAsync"
        };

        var type = typeof(Services.CimmeriaSummarizationService);
        foreach (var methodName in expectedMethods)
        {
            Assert.True(type.GetMethod(methodName) is not null,
                $"Missing AI skill method: {methodName}");
        }
    }

    [Fact]
    public void BigWorldPreamble_ContainsEntityArchitecture()
    {
        var field = typeof(Services.CimmeriaSummarizationService)
            .GetField("BigWorldPreamble", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);

        var preamble = (string)field!.GetValue(null)!;
        Assert.Contains("Base", preamble);
        Assert.Contains("Cell", preamble);
        Assert.Contains("Client", preamble);
        Assert.Contains("CELL_PUBLIC", preamble);
    }
}
