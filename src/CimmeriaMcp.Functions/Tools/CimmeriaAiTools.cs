using CimmeriaMcp.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Extensions.Mcp;

namespace CimmeriaMcp.Functions.Tools;

public class CimmeriaAiTools
{
    private readonly CimmeriaSummarizationService _aiService;

    public CimmeriaAiTools(CimmeriaSummarizationService aiService)
    {
        _aiService = aiService;
    }

    // ====================================================================
    // General Knowledge
    // ====================================================================

    [Function(nameof(ExplainCimmeria))]
    public async Task<string> ExplainCimmeria(
        [McpToolTrigger("explain_cimmeria",
            "Ask a question about the Cimmeria/SGW codebase and get an AI-synthesized answer. Uses RAG search + knowledge graph + GPT to provide coherent explanations. Good for 'how does X work?', 'explain the Y system', 'what is the relationship between A and B?'.")]
        ToolInvocationContext context,
        [McpToolProperty("question", "Natural language question about the codebase", isRequired: true)] string question,
        [McpToolProperty("focus", "Optional: focus on a source — cimmeria-server, sgw-client, or bigworld-engine")] string? focus)
    {
        return await _aiService.ExplainAsync(question, focus);
    }

    // ====================================================================
    // Code Generation
    // ====================================================================

    [Function(nameof(GenerateEntityStub))]
    public async Task<string> GenerateEntityStub(
        [McpToolTrigger("generate_entity_stub",
            "Generate a Rust struct/impl stub for an SGW entity, based on its .def file definition. Outputs Rust code with proper types, properties, traits, and method declarations.")]
        ToolInvocationContext context,
        [McpToolProperty("entity_name", "Entity name e.g. SGWPlayer, SGWMob", isRequired: true)] string entityName)
    {
        return await _aiService.GenerateEntityStubAsync(entityName);
    }

    [Function(nameof(TranslatePythonToRust))]
    public async Task<string> TranslatePythonToRust(
        [McpToolTrigger("translate_python_to_rust",
            "Convert Python BigWorld entity scripts to idiomatic Rust. Translates BigWorld API calls, entity communication patterns, and Python idioms to Rust equivalents. Can translate a full entity or a specific method.")]
        ToolInvocationContext context,
        [McpToolProperty("entity_name", "Entity name e.g. SGWPlayer, SGWMob", isRequired: true)] string entityName,
        [McpToolProperty("method_name", "Optional: specific method to translate. Omit for full entity.")] string? methodName)
    {
        return await _aiService.TranslatePythonToRustAsync(entityName, methodName);
    }

    [Function(nameof(GenerateTests))]
    public async Task<string> GenerateTests(
        [McpToolTrigger("generate_tests",
            "Generate Rust #[test] functions for an entity or method. Derives test cases from Python behavior, .def contracts, and protocol requirements. Covers unit tests, property tests, protocol tests, and integration tests.")]
        ToolInvocationContext context,
        [McpToolProperty("entity_name", "Entity name e.g. SGWPlayer, SGWMob", isRequired: true)] string entityName,
        [McpToolProperty("method_name", "Optional: specific method to generate tests for. Omit for full entity.")] string? methodName)
    {
        return await _aiService.GenerateTestsAsync(entityName, methodName);
    }

    // ====================================================================
    // Analysis & Debugging
    // ====================================================================

    [Function(nameof(Troubleshoot))]
    public async Task<string> Troubleshoot(
        [McpToolTrigger("troubleshoot",
            "Diagnose issues with Cimmeria server code. Describe the problem (error, crash, wrong behavior) and optionally paste code. Checks for protocol mismatches, implementation gaps, type errors, missing replicated properties, and BigWorld API differences.")]
        ToolInvocationContext context,
        [McpToolProperty("description", "Describe the problem — error message, unexpected behavior, crash details", isRequired: true)] string description,
        [McpToolProperty("code", "Optional: paste the problematic Rust code")] string? code,
        [McpToolProperty("entity_name", "Optional: entity involved (enables protocol & implementation checks)")] string? entityName)
    {
        return await _aiService.TroubleshootAsync(description, code, entityName);
    }

    [Function(nameof(ReviewCode))]
    public async Task<string> ReviewCode(
        [McpToolTrigger("review_code",
            "Review Rust code against original SGW .def specifications and Python behavior. Checks protocol correctness, property completeness, behavioral fidelity, type safety, and Rust idioms. Issues rated CRITICAL/WARNING/INFO.")]
        ToolInvocationContext context,
        [McpToolProperty("code", "Rust code to review", isRequired: true)] string code,
        [McpToolProperty("entity_name", "Optional: entity name for targeted spec checking")] string? entityName)
    {
        return await _aiService.ReviewCodeAsync(code, entityName);
    }

    [Function(nameof(CheckCompatibility))]
    public async Task<string> CheckCompatibility(
        [McpToolTrigger("check_compatibility",
            "Verify Rust code against the fixed SGW client binary. Checks every ClientMethod, exposed BaseMethod, CELL_PUBLIC property, and argument type against .def specs. The client CANNOT be modified — any mismatch causes crashes or disconnects. Returns a compatibility score.")]
        ToolInvocationContext context,
        [McpToolProperty("code", "Rust code to verify", isRequired: true)] string code,
        [McpToolProperty("entity_name", "Entity name to check against", isRequired: true)] string entityName)
    {
        return await _aiService.CheckCompatibilityAsync(code, entityName);
    }

    [Function(nameof(AnalyzeImpact))]
    public async Task<string> AnalyzeImpact(
        [McpToolTrigger("analyze_impact",
            "Analyze the impact of changing a method or property. Traces all dependents through call chains, cross-entity references, client visibility, and inheritance. Rates cascade risk LOW/MEDIUM/HIGH/CRITICAL.")]
        ToolInvocationContext context,
        [McpToolProperty("entity_name", "Entity that owns the method/property", isRequired: true)] string entityName,
        [McpToolProperty("target_name", "Method or property name to analyze", isRequired: true)] string targetName)
    {
        return await _aiService.AnalyzeImpactAsync(entityName, targetName);
    }

    // ====================================================================
    // Architecture & Planning
    // ====================================================================

    [Function(nameof(PlanImplementation))]
    public async Task<string> PlanImplementation(
        [McpToolTrigger("plan_implementation",
            "Create a detailed Rust implementation plan for an SGW entity. Includes struct definition, traits, method priority, dependencies, data flow, behavioral notes from Python, test strategy, and file structure. Ordered by implementation priority with client-critical items flagged.")]
        ToolInvocationContext context,
        [McpToolProperty("entity_name", "Entity name e.g. SGWPlayer, SGWMob, Account", isRequired: true)] string entityName)
    {
        return await _aiService.PlanImplementationAsync(entityName);
    }

    [Function(nameof(WhatsNext))]
    public async Task<string> WhatsNext(
        [McpToolTrigger("whats_next",
            "Recommend what to implement next based on current coverage, dependency impact, and game importance. Analyzes all entities' implementation status and ranks them by client-criticality, dependency count, Python coverage, and game system impact.")]
        ToolInvocationContext context)
    {
        return await _aiService.SuggestNextAsync();
    }

    [Function(nameof(AnalyzeProtocol))]
    public async Task<string> AnalyzeProtocol(
        [McpToolTrigger("analyze_protocol",
            "Analyze the client-server protocol for an entity or game system. Shows every message (client<->server), data contracts, sequence diagrams, critical compatibility notes, and serialization requirements. Accepts entity names (SGWPlayer) or system names (combat, inventory).")]
        ToolInvocationContext context,
        [McpToolProperty("entity_or_system", "Entity name (SGWPlayer) or game system (combat, inventory)", isRequired: true)] string entityOrSystem)
    {
        return await _aiService.AnalyzeProtocolAsync(entityOrSystem);
    }

    // ====================================================================
    // Visualization & Design
    // ====================================================================

    [Function(nameof(TraceSequence))]
    public async Task<string> TraceSequence(
        [McpToolTrigger("trace_sequence",
            "Trace the complete message sequence for a game scenario. Shows trigger, step-by-step method calls with direction and data, branching paths, client updates, and timing. E.g. 'player loots a mob', 'player uses an ability', 'player logs in'.")]
        ToolInvocationContext context,
        [McpToolProperty("scenario", "Game scenario to trace e.g. 'player attacks a mob', 'player trades with another player'", isRequired: true)] string scenario)
    {
        return await _aiService.TraceSequenceAsync(scenario);
    }

    [Function(nameof(GenerateDiagram))]
    public async Task<string> GenerateDiagram(
        [McpToolTrigger("generate_diagram",
            "Generate a Mermaid diagram for entities, systems, or flows. Supports class diagrams (inheritance/interfaces), sequence diagrams (message flows), flowcharts (logic), state diagrams, and dependency graphs. Output can be rendered in any Mermaid-compatible tool.")]
        ToolInvocationContext context,
        [McpToolProperty("subject", "What to diagram e.g. 'combat system', 'SGWPlayer inheritance', 'login flow'", isRequired: true)] string subject,
        [McpToolProperty("diagram_type", "Optional: classDiagram, sequenceDiagram, flowchart, stateDiagram-v2, graph. Default: auto-detect.")] string? diagramType)
    {
        return await _aiService.GenerateDiagramAsync(subject, diagramType);
    }

    [Function(nameof(DecodeGameDesign))]
    public async Task<string> DecodeGameDesign(
        [McpToolTrigger("decode_game_design",
            "Reverse-engineer the game design of an SGW feature from its code. Reconstructs the player experience: what players see, gameplay loops, rules/mechanics, progression, system interactions, and data models. Written for game designers, not programmers.")]
        ToolInvocationContext context,
        [McpToolProperty("system_or_feature", "Game system or feature e.g. 'combat', 'missions', 'trading', 'gate travel', 'inventory'", isRequired: true)] string systemOrFeature)
    {
        return await _aiService.DecodeGameDesignAsync(systemOrFeature);
    }
}
