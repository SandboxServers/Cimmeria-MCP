using System.ComponentModel;
using CimmeriaMcp.Services;
using ModelContextProtocol.Server;

namespace CimmeriaMcp.Tools;

/// <summary>
/// AI skill MCP tools. The underlying
/// <see cref="CimmeriaSummarizationService"/> is a stub awaiting its
/// own port (see that class's doc for the boundary), but the tool
/// catalogue stays intact so MCP clients see the same 14 AI tools
/// they always have.
/// </summary>
[McpServerToolType]
public sealed class CimmeriaAiTools
{
    private readonly CimmeriaSummarizationService _ai;

    public CimmeriaAiTools(CimmeriaSummarizationService ai)
    {
        _ai = ai;
    }

    [McpServerTool(Name = "explain_cimmeria")]
    [Description("Ask a question about the Cimmeria/SGW codebase and get an AI-synthesized answer. Uses RAG search + knowledge graph + GPT to provide coherent explanations.")]
    public Task<string> ExplainCimmeria(
        [Description("Natural language question about the codebase")] string question,
        [Description("Optional: focus on a source — cimmeria-server, sgw-client, or bigworld-engine")] string? focus = null)
        => _ai.ExplainAsync(question, focus);

    [McpServerTool(Name = "generate_entity_stub")]
    [Description("Generate a Rust struct/impl stub for an SGW entity, based on its .def file definition.")]
    public Task<string> GenerateEntityStub(
        [Description("Entity name e.g. SGWPlayer, SGWMob")] string entityName)
        => _ai.GenerateEntityStubAsync(entityName);

    [McpServerTool(Name = "translate_python_to_rust")]
    [Description("Convert Python BigWorld entity scripts to idiomatic Rust. Translates BigWorld API calls, entity communication patterns, and Python idioms to Rust equivalents.")]
    public Task<string> TranslatePythonToRust(
        [Description("Entity name e.g. SGWPlayer, SGWMob")] string entityName,
        [Description("Optional: specific method to translate. Omit for full entity.")] string? methodName = null)
        => _ai.TranslatePythonToRustAsync(entityName, methodName);

    [McpServerTool(Name = "generate_tests")]
    [Description("Generate Rust #[test] functions for an entity or method. Derives test cases from Python behavior, .def contracts, and protocol requirements.")]
    public Task<string> GenerateTests(
        [Description("Entity name e.g. SGWPlayer, SGWMob")] string entityName,
        [Description("Optional: specific method to generate tests for. Omit for full entity.")] string? methodName = null)
        => _ai.GenerateTestsAsync(entityName, methodName);

    [McpServerTool(Name = "troubleshoot")]
    [Description("Diagnose issues with Cimmeria server code. Describe the problem (error, crash, wrong behavior) and optionally paste code.")]
    public Task<string> Troubleshoot(
        [Description("Describe the problem — error message, unexpected behavior, crash details")] string description,
        [Description("Optional: paste the problematic Rust code")] string? code = null,
        [Description("Optional: entity involved (enables protocol & implementation checks)")] string? entityName = null)
        => _ai.TroubleshootAsync(description, code, entityName);

    [McpServerTool(Name = "review_code")]
    [Description("Review Rust code against original SGW .def specifications and Python behavior. Issues rated CRITICAL/WARNING/INFO.")]
    public Task<string> ReviewCode(
        [Description("Rust code to review")] string code,
        [Description("Optional: entity name for targeted spec checking")] string? entityName = null)
        => _ai.ReviewCodeAsync(code, entityName);

    [McpServerTool(Name = "check_compatibility")]
    [Description("Verify Rust code against the fixed SGW client binary. Checks every ClientMethod, exposed BaseMethod, CELL_PUBLIC property, and argument type against .def specs.")]
    public Task<string> CheckCompatibility(
        [Description("Rust code to verify")] string code,
        [Description("Entity name to check against")] string entityName)
        => _ai.CheckCompatibilityAsync(code, entityName);

    [McpServerTool(Name = "analyze_impact")]
    [Description("Analyze the impact of changing a method or property. Traces all dependents through call chains, cross-entity references, client visibility, and inheritance.")]
    public Task<string> AnalyzeImpact(
        [Description("Entity that owns the method/property")] string entityName,
        [Description("Method or property name to analyze")] string targetName)
        => _ai.AnalyzeImpactAsync(entityName, targetName);

    [McpServerTool(Name = "plan_implementation")]
    [Description("Create a detailed Rust implementation plan for an SGW entity. Includes struct definition, traits, method priority, dependencies, data flow, behavioral notes from Python, test strategy, and file structure.")]
    public Task<string> PlanImplementation(
        [Description("Entity name e.g. SGWPlayer, SGWMob, Account")] string entityName)
        => _ai.PlanImplementationAsync(entityName);

    [McpServerTool(Name = "whats_next")]
    [Description("Recommend what to implement next based on current coverage, dependency impact, and game importance.")]
    public Task<string> WhatsNext()
        => _ai.SuggestNextAsync();

    [McpServerTool(Name = "analyze_protocol")]
    [Description("Analyze the client-server protocol for an entity or game system.")]
    public Task<string> AnalyzeProtocol(
        [Description("Entity name (SGWPlayer) or game system (combat, inventory)")] string entityOrSystem)
        => _ai.AnalyzeProtocolAsync(entityOrSystem);

    [McpServerTool(Name = "trace_sequence")]
    [Description("Trace the complete message sequence for a game scenario. Shows trigger, step-by-step method calls with direction and data, branching paths, client updates, and timing.")]
    public Task<string> TraceSequence(
        [Description("Game scenario to trace e.g. 'player attacks a mob', 'player trades with another player'")] string scenario)
        => _ai.TraceSequenceAsync(scenario);

    [McpServerTool(Name = "generate_diagram")]
    [Description("Generate a Mermaid diagram for entities, systems, or flows.")]
    public Task<string> GenerateDiagram(
        [Description("What to diagram e.g. 'combat system', 'SGWPlayer inheritance', 'login flow'")] string subject,
        [Description("Optional: classDiagram, sequenceDiagram, flowchart, stateDiagram-v2, graph. Default: auto-detect.")] string? diagramType = null)
        => _ai.GenerateDiagramAsync(subject, diagramType);

    [McpServerTool(Name = "decode_game_design")]
    [Description("Reverse-engineer the game design of an SGW feature from its code. Reconstructs the player experience: what players see, gameplay loops, rules/mechanics, progression, system interactions, and data models.")]
    public Task<string> DecodeGameDesign(
        [Description("Game system or feature e.g. 'combat', 'missions', 'trading', 'gate travel', 'inventory'")] string systemOrFeature)
        => _ai.DecodeGameDesignAsync(systemOrFeature);
}
