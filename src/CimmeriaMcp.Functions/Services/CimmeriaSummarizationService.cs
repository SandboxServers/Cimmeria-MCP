using System.Text.Json;
using Azure;
using Azure.AI.OpenAI;
using Npgsql;
using OpenAI.Chat;
using OpenAI.Embeddings;

namespace CimmeriaMcp.Services;

/// <summary>
/// AI skill engine — GPT-backed analysis, code generation, planning,
/// and design extraction. The cloud deployment ran ~1300 lines of
/// prompt-engineering + Cosmos query glue here; this rewrite keeps
/// the constructor + OpenAI client wiring but stubs each AI method
/// out pending a follow-up port.
///
/// Why this is a stub, not a port:
///
/// Every AI method gathers context from the search + graph data layer
/// before assembling its prompt. That context-gathering code in the
/// cloud deployment uses Cosmos-specific SQL (`c.doc_type = 'vertex'`,
/// `c.label = 'method'`, `STARTSWITH`, etc.) which does not translate
/// 1:1 to the Postgres schema introduced here — the vertex/edge split
/// into separate tables changes every query. Doing those rewrites
/// correctly, keeping each prompt's evidence-quality intact, is its
/// own coordinated piece of work.
///
/// What works after this PR:
///   - The 6 search tools + 14 graph tools run against the Postgres
///     backend end-to-end.
///   - The 14 AI tools surface in `tools/list` (so MCP clients see
///     the catalogue is intact) but return a structured error envelope
///     directing the caller at the follow-up issue.
///
/// What follow-up needs to do (per method, ~14 methods × ~60 lines):
///   1. Rewrite the Cosmos QueryDefinition glue with Npgsql against
///      kg_vertices / kg_edges / code_chunks.
///   2. Replace `dynamic` accessors with JsonElement on the rows
///      returned by the new helpers.
///   3. Keep the prompt strings, OpenAI calls, and ResponseFormat
///      shape intact — those are all known-good and the LLM tooling
///      that consumes their output depends on them.
/// </summary>
public sealed class CimmeriaSummarizationService
{
    private const string EmbeddingModel = "text-embedding-3-small";
    private const string DefaultChatDeployment = "gpt-5.1-chat";
    private const string DefaultCodexDeployment = "gpt-5.1-codex-mini";

    private readonly NpgsqlDataSource _db;
    private readonly EmbeddingClient _embeddingClient;
    private readonly ChatClient _chatClient;
    private readonly ChatClient _codexClient;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public CimmeriaSummarizationService(NpgsqlDataSource db, IConfiguration config)
    {
        _db = db;

        var openAiEndpoint = config["OPENAI_ENDPOINT"]
            ?? throw new InvalidOperationException("OPENAI_ENDPOINT is not configured.");
        var openAiKey = config["OPENAI_KEY"]
            ?? throw new InvalidOperationException("OPENAI_KEY is not configured.");
        var embeddingDeployment = config["OPENAI_EMBEDDING_DEPLOYMENT"] ?? EmbeddingModel;
        var chatDeployment = config["OPENAI_CHAT_DEPLOYMENT"] ?? DefaultChatDeployment;
        var codexDeployment = config["OPENAI_CODEX_DEPLOYMENT"] ?? DefaultCodexDeployment;

        var azureOpenAi = new AzureOpenAIClient(new Uri(openAiEndpoint), new AzureKeyCredential(openAiKey));
        _embeddingClient = azureOpenAi.GetEmbeddingClient(embeddingDeployment);
        _chatClient = azureOpenAi.GetChatClient(chatDeployment);
        _codexClient = azureOpenAi.GetChatClient(codexDeployment);
    }

    // ──────────────────────────────────────────────────────────────
    // Stub responses
    // ──────────────────────────────────────────────────────────────

    private static string NotYetPorted(string skill, string explanation) =>
        JsonSerializer.Serialize(new
        {
            skill,
            status = "port_pending",
            message = $"The '{skill}' AI skill has not yet been ported from the Azure / Cosmos cloud deployment to the colo Postgres stack.",
            detail = explanation,
            available_alternatives = "The 6 search tools (search_cimmeria, find_similar_code, …) and 14 knowledge-graph tools (get_entity_details, traverse_graph, …) are fully ported and return real data from Postgres + pgvector. Use those to gather the same evidence the AI skill would have synthesised.",
            tracking = "See Services/CimmeriaSummarizationService.cs class doc for the port checklist.",
        }, JsonOptions);

    public Task<string> ExplainAsync(string question, string? focus)
        => Task.FromResult(NotYetPorted("explain_cimmeria",
            $"Would RAG-search for '{question}'{(focus is null ? "" : $" focused on {focus}")} and pass results through gpt-5.1-chat."));

    public Task<string> GenerateEntityStubAsync(string entityName)
        => Task.FromResult(NotYetPorted("generate_entity_stub",
            $"Would fetch {entityName}'s .def + Python + KG context and pass through gpt-5.1-codex-mini."));

    public Task<string> TranslatePythonToRustAsync(string entityName, string? methodName)
        => Task.FromResult(NotYetPorted("translate_python_to_rust",
            $"Would fetch {entityName}.{methodName ?? "*"} Python source via search_cimmeria + KG and call gpt-5.1-codex-mini."));

    public Task<string> GenerateTestsAsync(string entityName, string? methodName)
        => Task.FromResult(NotYetPorted("generate_tests",
            $"Would derive test cases for {entityName}.{methodName ?? "*"} from .def contract + Python behaviour + KG."));

    public Task<string> TroubleshootAsync(string description, string? code, string? entityName)
        => Task.FromResult(NotYetPorted("troubleshoot",
            $"Would diagnose '{description}'{(entityName is null ? "" : $" against {entityName}")} via cross-ref + protocol check."));

    public Task<string> ReviewCodeAsync(string code, string? entityName)
        => Task.FromResult(NotYetPorted("review_code",
            $"Would review against {entityName ?? "no specific entity"} spec + Python behaviour."));

    public Task<string> CheckCompatibilityAsync(string code, string entityName)
        => Task.FromResult(NotYetPorted("check_compatibility",
            $"Would diff every ClientMethod / Exposed BaseMethod / CELL_PUBLIC property against {entityName}'s .def spec."));

    public Task<string> AnalyzeImpactAsync(string entityName, string targetName)
        => Task.FromResult(NotYetPorted("analyze_impact",
            $"Would trace dependents of {entityName}.{targetName} through call chains + cross-ref + inheritance."));

    public Task<string> PlanImplementationAsync(string entityName)
        => Task.FromResult(NotYetPorted("plan_implementation",
            $"Would synthesise a Rust implementation plan for {entityName} from .def + Python + protocol."));

    public Task<string> SuggestNextAsync()
        => Task.FromResult(NotYetPorted("whats_next",
            "Would rank all entities by client-criticality + dependency count + implementation coverage."));

    public Task<string> AnalyzeProtocolAsync(string entityOrSystem)
        => Task.FromResult(NotYetPorted("analyze_protocol",
            $"Would emit the full client/server message contract for {entityOrSystem}."));

    public Task<string> TraceSequenceAsync(string scenario)
        => Task.FromResult(NotYetPorted("trace_sequence",
            $"Would trace the message sequence for scenario '{scenario}' across cell/base/client."));

    public Task<string> GenerateDiagramAsync(string subject, string? diagramType)
        => Task.FromResult(NotYetPorted("generate_diagram",
            $"Would generate a {diagramType ?? "auto-detected"} Mermaid diagram for '{subject}'."));

    public Task<string> DecodeGameDesignAsync(string systemOrFeature)
        => Task.FromResult(NotYetPorted("decode_game_design",
            $"Would reverse-engineer the game design of '{systemOrFeature}' from code + KG."));

    // ──────────────────────────────────────────────────────────────
    // Helper kept for the follow-up port
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Standard chat completion call — the follow-up port will wire
    /// this back into every AI method. Kept here so the prompt-engineering
    /// PR doesn't need to re-discover the right OpenAI SDK shapes.
    /// </summary>
    internal async Task<string> CallGptAsync(string systemPrompt, string userPrompt, int maxTokens = 3000, float temperature = 0.3f)
    {
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt),
        };
        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = maxTokens,
            Temperature = temperature,
        };
        var response = await _chatClient.CompleteChatAsync(messages, options);
        return response.Value.Content[0].Text;
    }

    /// <summary>Same as <see cref="CallGptAsync"/> but routed to the codex deployment for code-gen skills.</summary>
    internal async Task<string> CallCodexAsync(string systemPrompt, string userPrompt, int maxTokens = 4000, float temperature = 0.2f)
    {
        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userPrompt),
        };
        var options = new ChatCompletionOptions
        {
            MaxOutputTokenCount = maxTokens,
            Temperature = temperature,
        };
        var response = await _codexClient.CompleteChatAsync(messages, options);
        return response.Value.Content[0].Text;
    }
}
