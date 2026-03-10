using Azure;
using Azure.AI.OpenAI;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using OpenAI.Chat;
using OpenAI.Embeddings;

namespace CimmeriaMcp.Functions.Services;

public class CimmeriaSummarizationService
{
    private const string DatabaseName = "cimmeria";
    private const string CodeChunksContainer = "code-chunks";
    private const string GraphContainer = "knowledge-graph";
    private const string EmbeddingModel = "text-embedding-3-small";
    private const string ChatModel = "gpt-5-1-chat";
    private const string CodexModel = "gpt-5-1-codex-mini";

    private readonly Container _codeContainer;
    private readonly Container _graphContainer;
    private readonly EmbeddingClient _embeddingClient;
    private readonly ChatClient _chatClient;
    private readonly ChatClient _codexClient;

    private const string BigWorldPreamble = """
        BigWorld Architecture:
        - Entities have Base (persistent/login), Cell (spatial/gameplay), and Client parts
        - Methods: ClientMethods (server->client), CellMethods (gameplay logic), BaseMethods (persistence)
        - Exposed BaseMethods are callable by the client
        - Properties with CELL_PUBLIC flag are automatically replicated to the client
        - Python scripts in cell/ implement gameplay, base/ implement persistence
        - The Cimmeria reimplementation targets Rust, not C++
        """;

    private const string ResponseFormatInstruction = """

        RESPONSE FORMAT — you MUST follow this structure exactly:

        ## Summary
        A 1-3 sentence high-level answer.

        ## Details
        Your detailed analysis, code, or findings organized with clear subheadings.

        ## Sources & Evidence
        - List specific file paths, method names, properties, or graph data that support your answer
        - Reference .def definitions, Python scripts, or Rust code by name

        ## Confidence
        Rate your confidence: HIGH (directly supported by code/data), MEDIUM (inferred from patterns),
        or LOW (speculative / insufficient context). Briefly explain what would increase confidence.
        """;

    public CimmeriaSummarizationService()
    {
        var cosmosEndpoint = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT")
            ?? throw new InvalidOperationException("COSMOS_ENDPOINT is not configured.");
        var cosmosKey = Environment.GetEnvironmentVariable("COSMOS_KEY")
            ?? throw new InvalidOperationException("COSMOS_KEY is not configured.");
        var openAiEndpoint = Environment.GetEnvironmentVariable("OPENAI_ENDPOINT")
            ?? throw new InvalidOperationException("OPENAI_ENDPOINT is not configured.");
        var openAiKey = Environment.GetEnvironmentVariable("OPENAI_KEY")
            ?? throw new InvalidOperationException("OPENAI_KEY is not configured.");

        var cosmosClient = new CosmosClient(cosmosEndpoint, cosmosKey);
        _codeContainer = cosmosClient.GetContainer(DatabaseName, CodeChunksContainer);
        _graphContainer = cosmosClient.GetContainer(DatabaseName, GraphContainer);

        var azureOpenAiClient = new AzureOpenAIClient(
            new Uri(openAiEndpoint),
            new AzureKeyCredential(openAiKey));
        _embeddingClient = azureOpenAiClient.GetEmbeddingClient(EmbeddingModel);
        _chatClient = azureOpenAiClient.GetChatClient(ChatModel);
        _codexClient = azureOpenAiClient.GetChatClient(CodexModel);
    }

    // ====================================================================
    // Shared Helpers
    // ====================================================================

    private async Task<float[]> GetEmbeddingAsync(string text)
    {
        var options = new EmbeddingGenerationOptions { Dimensions = 505 };
        var response = await _embeddingClient.GenerateEmbeddingAsync(text, options);
        return response.Value.ToFloats().ToArray();
    }

    private async Task<List<T>> QueryGraphAsync<T>(QueryDefinition query)
    {
        var results = new List<T>();
        using var iter = _graphContainer.GetItemQueryIterator<T>(query);
        while (iter.HasMoreResults)
        {
            var resp = await iter.ReadNextAsync();
            results.AddRange(resp);
        }
        return results;
    }

    private async Task<List<(string path, string content, string source, double distance)>> SearchCodeAsync(
        string query, string? focus = null, int topN = 6)
    {
        var embedding = await GetEmbeddingAsync(query);
        var whereClause = !string.IsNullOrEmpty(focus) ? $"WHERE c.source_project = '{focus}'" : "";

        var sql = $"SELECT TOP {topN} c.file_path, c.content, c.source_project, " +
                  $"VectorDistance(c.embedding, @embedding) AS distance " +
                  $"FROM c {whereClause} " +
                  $"ORDER BY VectorDistance(c.embedding, @embedding)";

        var results = new List<(string path, string content, string source, double distance)>();
        using var iter = _codeContainer.GetItemQueryIterator<dynamic>(
            new QueryDefinition(sql).WithParameter("@embedding", embedding));
        while (iter.HasMoreResults)
        {
            var resp = await iter.ReadNextAsync();
            foreach (var item in resp)
                results.Add(((string)item.file_path, (string)item.content,
                    (string)item.source_project, (double)item.distance));
        }
        return results;
    }

    private string FormatCodeResults(List<(string path, string content, string source, double distance)> results)
    {
        return string.Join("\n\n", results.Select(r =>
            $"--- [{r.source}] {r.path} (relevance: {1 - r.distance:F3}) ---\n{r.content}"));
    }

    private async Task<List<string>> SearchGraphEntitiesAsync(string question)
    {
        var context = new List<string>();

        var entitySql = "SELECT c.name, c.label, c.property_count, c.client_method_count, c.cell_method_count, c.base_method_count " +
                        "FROM c WHERE c.doc_type = 'vertex' AND c.label IN ('entity', 'interface') AND CONTAINS(UPPER(@question), UPPER(c.name))";
        var entities = await QueryGraphAsync<dynamic>(
            new QueryDefinition(entitySql).WithParameter("@question", question));
        foreach (var item in entities)
        {
            context.Add($"Entity: {item.name} ({item.label}) — {item.property_count} properties, " +
                        $"{item.client_method_count} client methods, {item.cell_method_count} cell methods, " +
                        $"{item.base_method_count} base methods");
        }

        var systemSql = "SELECT c.name, c.description, c.keywords FROM c WHERE c.doc_type = 'vertex' AND c.label = 'game_system'";
        var systems = await QueryGraphAsync<dynamic>(new QueryDefinition(systemSql));
        foreach (var item in systems)
        {
            string name = (string)item.name;
            string desc = (string)item.description;
            if (question.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                desc.Split(' ').Any(w => question.Contains(w, StringComparison.OrdinalIgnoreCase)))
            {
                context.Add($"Game System: {name} — {desc}");
            }
        }

        return context;
    }

    private async Task<(dynamic? entity, List<dynamic> props, List<dynamic> methods, string? parent, List<string> interfaces)>
        GetEntityContextAsync(string entityName)
    {
        dynamic? entity = null;
        var entityResults = await QueryGraphAsync<dynamic>(
            new QueryDefinition("SELECT * FROM c WHERE c.id = @id AND c.doc_type = 'vertex'")
                .WithParameter("@id", $"entity:{entityName}"));
        if (entityResults.Count > 0) entity = entityResults[0];

        if (entity == null)
        {
            entityResults = await QueryGraphAsync<dynamic>(
                new QueryDefinition("SELECT * FROM c WHERE c.id = @id AND c.doc_type = 'vertex'")
                    .WithParameter("@id", $"interface:{entityName}"));
            if (entityResults.Count > 0) entity = entityResults[0];
        }

        var props = await QueryGraphAsync<dynamic>(
            new QueryDefinition("SELECT * FROM c WHERE c.label = 'property' AND c.owner = @owner AND c.doc_type = 'vertex'")
                .WithParameter("@owner", entityName));

        var methods = await QueryGraphAsync<dynamic>(
            new QueryDefinition("SELECT * FROM c WHERE c.label = 'method' AND c.owner = @owner AND c.doc_type = 'vertex'")
                .WithParameter("@owner", entityName));

        string? parent = null;
        var inheritEdges = await QueryGraphAsync<dynamic>(
            new QueryDefinition("SELECT * FROM c WHERE c.from_id = @fromId AND c.label = 'inherits' AND c.doc_type = 'edge'")
                .WithParameter("@fromId", $"entity:{entityName}"));
        if (inheritEdges.Count > 0)
            parent = ((string)inheritEdges[0].to_id).Replace("entity:", "");

        var ifaceEdges = await QueryGraphAsync<dynamic>(
            new QueryDefinition("SELECT c.to_id FROM c WHERE c.from_id = @fromId AND c.label = 'implements' AND c.doc_type = 'edge'")
                .WithParameter("@fromId", $"entity:{entityName}"));
        var interfaces = ifaceEdges.Select(e => ((string)e.to_id).Replace("interface:", "")).ToList();

        return (entity, props, methods, parent, interfaces);
    }

    private async Task<string> GetImplementationContextAsync(string entityName)
    {
        var defMethods = await QueryGraphAsync<dynamic>(
            new QueryDefinition("SELECT c.name, c.method_type FROM c WHERE c.label = 'method' AND c.owner = @owner AND c.doc_type = 'vertex'")
                .WithParameter("@owner", entityName));

        var pyMethods = await QueryGraphAsync<dynamic>(
            new QueryDefinition("SELECT c.name FROM c WHERE c.label = 'script_method' AND c.owner = @owner AND c.doc_type = 'vertex'")
                .WithParameter("@owner", entityName));
        var pySet = new HashSet<string>(pyMethods.Select(m => (string)m.name));

        var cppMethods = await QueryGraphAsync<dynamic>(
            new QueryDefinition("SELECT c.name FROM c WHERE c.label = 'cpp_method' AND c.owner = @owner AND c.doc_type = 'vertex'")
                .WithParameter("@owner", entityName));
        var cppSet = new HashSet<string>(cppMethods.Select(m => (string)m.name));

        var lines = new List<string> { $"Implementation status for {entityName}:" };
        foreach (var m in defMethods)
        {
            string name = (string)m.name;
            string type = (string)m.method_type;
            bool hasPy = pySet.Contains(name);
            bool hasCpp = cppSet.Contains(name);
            string status = (hasPy, hasCpp) switch
            {
                (true, true) => "Python + C++",
                (true, false) => "Python only",
                (false, true) => "C++ only",
                _ => "NOT IMPLEMENTED",
            };
            lines.Add($"  {type} {name}: {status}");
        }
        return string.Join("\n", lines);
    }

    private async Task<string> GetProtocolContextAsync(string entityName)
    {
        var methods = await QueryGraphAsync<dynamic>(
            new QueryDefinition("SELECT c.name, c.method_type, c.args FROM c WHERE c.label = 'method' AND c.owner = @owner AND c.doc_type = 'vertex'")
                .WithParameter("@owner", entityName));

        var props = await QueryGraphAsync<dynamic>(
            new QueryDefinition("SELECT c.name, c.data_type, c.flags FROM c WHERE c.label = 'property' AND c.owner = @owner AND c.doc_type = 'vertex'")
                .WithParameter("@owner", entityName));

        var lines = new List<string> { $"Protocol for {entityName}:" };

        var clientMethods = methods.Where(m => (string)m.method_type == "ClientMethod").ToList();
        if (clientMethods.Count > 0)
        {
            lines.Add("  Server->Client (ClientMethods):");
            foreach (var m in clientMethods) lines.Add($"    {m.name}({m.args})");
        }

        var baseMethods = methods.Where(m => (string)m.method_type == "BaseMethod").ToList();
        if (baseMethods.Count > 0)
        {
            lines.Add("  Client->Server (Exposed BaseMethods):");
            foreach (var m in baseMethods) lines.Add($"    {m.name}({m.args})");
        }

        var cellMethods = methods.Where(m => (string)m.method_type == "CellMethod").ToList();
        if (cellMethods.Count > 0)
        {
            lines.Add("  Cell Methods (gameplay):");
            foreach (var m in cellMethods) lines.Add($"    {m.name}({m.args})");
        }

        var replicatedProps = props.Where(p => ((string)p.flags).Contains("CELL_PUBLIC")).ToList();
        if (replicatedProps.Count > 0)
        {
            lines.Add("  Auto-Replicated Properties (CELL_PUBLIC):");
            foreach (var p in replicatedProps) lines.Add($"    {p.name}: {p.data_type}");
        }

        return string.Join("\n", lines);
    }

    private string BuildEntitySpec(string entityName, string? parent, List<string> interfaces,
        List<dynamic> props, List<dynamic> methods)
    {
        return JsonConvert.SerializeObject(new
        {
            name = entityName, parent, interfaces,
            properties = props.Select(p => new { name = (string)p.name, type = (string)p.data_type, flags = (string)p.flags }),
            methods = methods.Select(m => new { name = (string)m.name, type = (string)m.method_type, args = m.args }),
        }, Formatting.Indented);
    }

    private async Task<string> CallGptAsync(string systemPrompt, string userPrompt,
        int maxTokens = 3000, float temperature = 0.3f)
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

    private async Task<string> CallCodexAsync(string systemPrompt, string userPrompt,
        int maxTokens = 4000, float temperature = 0.2f)
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

    private string Respond(string skill, object result, string? modelOverride = null)
    {
        return JsonConvert.SerializeObject(new
        {
            skill,
            model = modelOverride ?? ChatModel,
            timestamp = DateTime.UtcNow.ToString("o"),
            result,
        }, Formatting.Indented);
    }

    /// <summary>
    /// Gathers optimal context based on what inputs are available.
    /// Returns a consolidated context block for any skill to use.
    /// </summary>
    private async Task<SkillContext> GatherContextAsync(
        string? query = null, string? entityName = null, string? code = null,
        string? focus = null, int codeTopN = 6)
    {
        var ctx = new SkillContext();

        // Always do RAG search if we have a query
        if (!string.IsNullOrEmpty(query))
        {
            ctx.CodeResults = await SearchCodeAsync(query, focus, codeTopN);
            ctx.GraphContext = await SearchGraphEntitiesAsync(query);
        }

        // If entity specified, get full entity context
        if (!string.IsNullOrEmpty(entityName))
        {
            var (entity, props, methods, parent, interfaces) = await GetEntityContextAsync(entityName);
            ctx.Entity = entity;
            ctx.Props = props;
            ctx.Methods = methods;
            ctx.Parent = parent;
            ctx.Interfaces = interfaces;

            if (entity != null)
            {
                ctx.ImplContext = await GetImplementationContextAsync(entityName);
                ctx.ProtocolContext = await GetProtocolContextAsync(entityName);
                ctx.EntitySpec = BuildEntitySpec(entityName, parent, interfaces, props, methods);
            }

            // Also add entity to graph context if not already there
            if (ctx.GraphContext.Count == 0)
                ctx.GraphContext = await SearchGraphEntitiesAsync(entityName);
        }

        // If code provided, also search for similar patterns
        if (!string.IsNullOrEmpty(code) && ctx.CodeResults.Count == 0)
        {
            ctx.CodeResults = await SearchCodeAsync(code, focus, codeTopN);
        }

        return ctx;
    }

    private class SkillContext
    {
        public List<(string path, string content, string source, double distance)> CodeResults { get; set; } = new();
        public List<string> GraphContext { get; set; } = new();
        public dynamic? Entity { get; set; }
        public List<dynamic> Props { get; set; } = new();
        public List<dynamic> Methods { get; set; } = new();
        public string? Parent { get; set; }
        public List<string> Interfaces { get; set; } = new();
        public string ImplContext { get; set; } = "";
        public string ProtocolContext { get; set; } = "";
        public string EntitySpec { get; set; } = "";

        public string FormatGraphContext() =>
            GraphContext.Count > 0 ? "\nKnowledge Graph:\n" + string.Join("\n", GraphContext) : "";

        public string FormatImplContext() =>
            ImplContext.Length > 0 ? $"\n{ImplContext}\n" : "";

        public string FormatProtocolContext() =>
            ProtocolContext.Length > 0 ? $"\n{ProtocolContext}\n" : "";
    }

    // ====================================================================
    // Skill 1: Explain
    // ====================================================================

    public async Task<string> ExplainAsync(string question, string? focus)
    {
        var ctx = await GatherContextAsync(query: question, focus: focus);

        var systemPrompt = $"""
            You are an expert on the Stargate Worlds (SGW) MMORPG codebase, built on the BigWorld engine.
            The codebase includes:
            - Cimmeria server: Rust reimplementation of the game server
            - Original Python scripts: Entity behavior (cell scripts for gameplay, base scripts for persistence)
            - Entity .def files: XML definitions of entity properties, methods, and inheritance
            - BigWorld engine: MMO engine source (networking, entity system, spatial management)
            - SGW client: Game client assets and UI

            {BigWorldPreamble}

            Answer questions accurately based on the provided code context. Be specific and reference actual
            code, method names, and file paths. If the context doesn't fully answer the question, say what
            you can determine and what would need further investigation.
            {ResponseFormatInstruction}
            """;

        var answer = await CallGptAsync(systemPrompt, $"""
            Question: {question}
            {ctx.FormatGraphContext()}

            Relevant Code:
            {FormatCodeResults(ctx.CodeResults)}
            """);

        return Respond("explain", new
        {
            answer,
            sources = ctx.CodeResults.Select(r => new { file = r.path, source = r.source, relevance = Math.Round(1 - r.distance, 3) }),
            graph_context = ctx.GraphContext,
        });
    }

    // ====================================================================
    // Skill 2: Generate Entity Stub (Rust)
    // ====================================================================

    public async Task<string> GenerateEntityStubAsync(string entityName)
    {
        var ctx = await GatherContextAsync(entityName: entityName);

        if (ctx.Entity == null)
            return Respond("generate_stub", new { error = $"Entity '{entityName}' not found." });

        var systemPrompt = $"""
            You are a Rust code generator for a BigWorld engine game server reimplementation (Cimmeria).
            Generate clean Rust code for the given entity definition.
            Conventions:
            - Use a struct for the entity with properties as fields
            - Use traits for interfaces the entity implements
            - Map BigWorld types: INT32->i32, UINT8->u8, FLOAT->f32, FLOAT64->f64, WSTRING->String,
              VECTOR3->Vec3, ARRAY<T>->Vec<T>, PYTHON->PyObject, MAILBOX->Mailbox,
              UINT16->u16, UINT32->u32, INT8->i8, INT16->i16, INT64->i64, BLOB->Vec<u8>
            - Group method declarations into impl blocks by type (ClientMethods, CellMethods, BaseMethods)
            - Use #[derive(Debug, Default)] on structs
            - If entity has a parent, include a parent field (composition over inheritance)
            - Include proper use statements and module references
            Only output the Rust code, no explanation.
            """;

        var code = await CallCodexAsync(systemPrompt,
            $"Generate Rust code for this entity:\n{ctx.EntitySpec}");

        return Respond("generate_stub", new
        {
            entity = entityName, parent = ctx.Parent, interfaces = ctx.Interfaces,
            property_count = ctx.Props.Count, method_count = ctx.Methods.Count,
            generated_code = code,
        }, CodexModel);
    }

    // ====================================================================
    // Skill 3: Troubleshoot
    // ====================================================================

    public async Task<string> TroubleshootAsync(string description, string? code, string? entityName)
    {
        var ctx = await GatherContextAsync(query: description, entityName: entityName, code: code, codeTopN: 8);

        var systemPrompt = $"""
            You are a senior debugging expert for the Cimmeria project — a Rust reimplementation of the
            Stargate Worlds (SGW) MMORPG server built on the BigWorld engine.

            {BigWorldPreamble}

            Your job is to diagnose issues. Consider:
            1. Protocol mismatches — is the server sending what the client expects?
            2. Implementation gaps — is a method defined in .def but not implemented?
            3. Type mismatches — are property types correct per the .def specification?
            4. Missing replicated properties — CELL_PUBLIC properties must be synced to client
            5. Incorrect method signatures — args must match the .def exactly or client disconnects
            6. BigWorld API behavioral differences — Python BigWorld.* calls vs Rust equivalents

            Be specific. Reference exact method names, property names, and file paths.
            Suggest concrete fixes with code snippets where appropriate (in Rust).
            {ResponseFormatInstruction}
            """;

        var answer = await CallGptAsync(systemPrompt, $"""
            Problem Description: {description}

            {(code != null ? $"Code:\n```\n{code}\n```\n" : "")}
            {ctx.FormatImplContext()}
            {ctx.FormatProtocolContext()}
            {ctx.FormatGraphContext()}

            Related Code from Codebase:
            {FormatCodeResults(ctx.CodeResults)}
            """);

        return Respond("troubleshoot", new
        {
            diagnosis = answer,
            entity = entityName,
            sources = ctx.CodeResults.Select(r => new { file = r.path, source = r.source, relevance = Math.Round(1 - r.distance, 3) }),
        });
    }

    // ====================================================================
    // Skill 4: Plan Implementation
    // ====================================================================

    public async Task<string> PlanImplementationAsync(string entityName)
    {
        var ctx = await GatherContextAsync(
            query: $"{entityName} cell script base script Python",
            entityName: entityName, codeTopN: 8);

        if (ctx.Entity == null)
            return Respond("plan_implementation", new { error = $"Entity '{entityName}' not found in knowledge graph." });

        // Get game system associations
        var systemEdges = await QueryGraphAsync<dynamic>(
            new QueryDefinition("SELECT c.to_id FROM c WHERE c.from_id = @fromId AND c.label = 'part_of_system' AND c.doc_type = 'edge'")
                .WithParameter("@fromId", $"entity:{entityName}"));
        var systemNames = systemEdges.Select(e => ((string)e.to_id).Replace("system:", "")).ToList();

        var systemPrompt = $"""
            You are a Rust architect planning the implementation of a BigWorld entity for the Cimmeria server.

            {BigWorldPreamble}

            Create a detailed implementation plan that includes:
            1. **Rust struct definition** with all properties mapped to Rust types
            2. **Traits to implement** based on interfaces
            3. **Method implementation priority** — which methods are critical for client compatibility
            4. **Dependencies** — what other entities/systems must exist first
            5. **Data flow** — how this entity communicates with client, base, and cell
            6. **Behavioral notes** from the Python scripts — edge cases, validation, state machines
            7. **Test strategy** — what to verify for client compatibility
            8. **Suggested file structure** and module organization

            Order the plan by implementation priority. Flag any methods that are critical for
            the client to not crash (ClientMethods, exposed BaseMethods, CELL_PUBLIC properties).
            {ResponseFormatInstruction}
            """;

        var answer = await CallGptAsync(systemPrompt, $"""
            Plan the Rust implementation for this entity:
            {ctx.EntitySpec}

            Game Systems: {string.Join(", ", systemNames)}

            {ctx.FormatImplContext()}
            {ctx.FormatProtocolContext()}

            Python Reference Code:
            {FormatCodeResults(ctx.CodeResults)}
            """, maxTokens: 4000);

        return Respond("plan_implementation", new
        {
            entity = entityName, parent = ctx.Parent, interfaces = ctx.Interfaces,
            game_systems = systemNames,
            property_count = ctx.Props.Count, method_count = ctx.Methods.Count,
            plan = answer,
        });
    }

    // ====================================================================
    // Skill 5: Review Code
    // ====================================================================

    public async Task<string> ReviewCodeAsync(string code, string? entityName)
    {
        var ctx = await GatherContextAsync(query: code, entityName: entityName, code: code);

        var systemPrompt = $"""
            You are a senior Rust code reviewer for the Cimmeria project — a reimplementation of the
            Stargate Worlds MMORPG server.

            {BigWorldPreamble}

            Review the provided Rust code against the original specifications. Check for:
            1. **Protocol correctness** — method signatures must exactly match .def files or client crashes
            2. **Property completeness** — all CELL_PUBLIC properties must be present and correctly typed
            3. **Behavioral fidelity** — does the Rust code replicate the Python behavior? Missing edge cases?
            4. **Type safety** — BigWorld types mapped correctly to Rust types?
            5. **Missing methods** — any .def methods not implemented that the client will call?
            6. **Rust idioms** — proper error handling, ownership, lifetime usage
            7. **Thread safety** — any shared state issues?

            For each issue, reference the exact method/property and what the .def/Python says
            vs what the Rust code does. Rate severity: CRITICAL (client crash), WARNING (incorrect behavior),
            INFO (style/improvement).
            {ResponseFormatInstruction}
            """;

        var answer = await CallGptAsync(systemPrompt, $"""
            Rust code to review:
            ```rust
            {code}
            ```

            {ctx.FormatImplContext()}
            {ctx.FormatProtocolContext()}
            {ctx.FormatGraphContext()}

            Original Reference Code:
            {FormatCodeResults(ctx.CodeResults)}
            """);

        return Respond("review_code", new
        {
            review = answer,
            entity = entityName,
            sources = ctx.CodeResults.Select(r => new { file = r.path, source = r.source, relevance = Math.Round(1 - r.distance, 3) }),
        });
    }

    // ====================================================================
    // Skill 6: Translate Python -> Rust
    // ====================================================================

    public async Task<string> TranslatePythonToRustAsync(string entityName, string? methodName)
    {
        var searchQuery = methodName != null
            ? $"{entityName} {methodName} Python cell base"
            : $"{entityName} Python cell base script";

        var ctx = await GatherContextAsync(query: searchQuery, entityName: entityName, codeTopN: 10);

        // BigWorld API usage
        var bwApiEdges = await QueryGraphAsync<dynamic>(
            new QueryDefinition("SELECT c.from_id, c.to_id FROM c WHERE c.label = 'uses_bigworld' AND CONTAINS(c.from_id, @entity) AND c.doc_type = 'edge'")
                .WithParameter("@entity", entityName));
        var bwApis = bwApiEdges.Select(e => (string)e.to_id).Distinct().ToList();

        // Resolve type aliases used in properties
        var typeNames = ctx.Props.Select(p => (string)p.data_type).Distinct().ToList();
        var typeContext = new List<string>();
        foreach (var t in typeNames)
        {
            var typeResults = await QueryGraphAsync<dynamic>(
                new QueryDefinition("SELECT c.name, c.alias_type, c.target_type FROM c WHERE c.name = @name AND c.label = 'type_alias' AND c.doc_type = 'vertex'")
                    .WithParameter("@name", t));
            foreach (var tr in typeResults)
                typeContext.Add($"Type {tr.name}: {tr.alias_type} -> {tr.target_type}");
        }

        var systemPrompt = $"""
            You are translating Python BigWorld entity scripts to Rust for the Cimmeria server.

            {BigWorldPreamble}

            Translation rules:
            - BigWorld.time() -> game_time() or SystemTime
            - BigWorld.addTimer(interval, repeat, userData) -> timer system with callback
            - BigWorld.createEntity(type, ...) -> entity spawner service
            - self.client.methodName(...) -> send ClientMethod RPC
            - self.base.methodName(...) -> send BaseMethod message
            - self.cell.methodName(...) -> send CellMethod message
            - self.allClients.methodName(...) -> broadcast to all clients in AoI
            - Python dict -> HashMap or struct
            - Python list -> Vec
            - Python None checks -> Option<T>
            - Python try/except -> Result<T, E> with ? operator

            Preserve the behavioral logic exactly. Add Rust error handling where Python would silently fail.
            Include comments explaining non-obvious BigWorld behavior.
            Output clean, idiomatic Rust code.
            {ResponseFormatInstruction}
            """;

        var answer = await CallCodexAsync(systemPrompt, $"""
            Translate {entityName}{(methodName != null ? $".{methodName}" : "")} from Python to Rust:

            Entity Definition:
            {ctx.EntitySpec}

            {(bwApis.Count > 0 ? "BigWorld APIs used: " + string.Join(", ", bwApis) : "")}
            {(typeContext.Count > 0 ? "\nType Aliases:\n" + string.Join("\n", typeContext) : "")}

            Python Source Code:
            {FormatCodeResults(ctx.CodeResults)}
            """);

        return Respond("translate_python_to_rust", new
        {
            entity = entityName, method = methodName,
            bigworld_apis_used = bwApis,
            rust_code = answer,
            python_sources = ctx.CodeResults.Select(r => new { file = r.path, relevance = Math.Round(1 - r.distance, 3) }),
        }, CodexModel);
    }

    // ====================================================================
    // Skill 7: Analyze Protocol
    // ====================================================================

    public async Task<string> AnalyzeProtocolAsync(string entityOrSystem)
    {
        var (entity, _, _, _, _) = await GetEntityContextAsync(entityOrSystem);
        var entityNames = new List<string>();

        if (entity != null)
        {
            entityNames.Add(entityOrSystem);
        }
        else
        {
            var systemEdges = await QueryGraphAsync<dynamic>(
                new QueryDefinition("SELECT c.from_id FROM c WHERE c.to_id = @systemId AND c.label = 'part_of_system' AND c.doc_type = 'edge'")
                    .WithParameter("@systemId", $"system:{entityOrSystem}"));
            entityNames = systemEdges.Select(e => ((string)e.from_id).Replace("entity:", "")).ToList();
        }

        if (entityNames.Count == 0)
            return Respond("analyze_protocol", new { error = $"'{entityOrSystem}' not found as entity or game system." });

        var protocols = new List<string>();
        foreach (var name in entityNames)
            protocols.Add(await GetProtocolContextAsync(name));

        var codeResults = await SearchCodeAsync($"{entityOrSystem} client method RPC protocol");

        var systemPrompt = $"""
            You are a network protocol analyst for the Cimmeria project — a Rust reimplementation of the
            Stargate Worlds MMORPG server that must be compatible with the original game client binary.

            {BigWorldPreamble}

            Analyze the client-server protocol and produce:
            1. **Message catalog** — every message between client and server, with direction and args
            2. **Data contracts** — exact property types the client expects to receive
            3. **Sequence diagrams** — typical interaction flows (login, combat round, loot, etc.)
            4. **Critical compatibility notes** — anything where getting it wrong crashes the client
            5. **Serialization requirements** — BigWorld's wire format expectations

            The client binary is FIXED. It cannot be modified. Every method signature, property type,
            and replication flag must match exactly.
            {ResponseFormatInstruction}
            """;

        var answer = await CallGptAsync(systemPrompt, $"""
            Analyze the protocol for: {entityOrSystem}
            Entities involved: {string.Join(", ", entityNames)}

            {string.Join("\n\n", protocols)}

            Related Code:
            {FormatCodeResults(codeResults)}
            """);

        return Respond("analyze_protocol", new
        {
            target = entityOrSystem,
            entities_analyzed = entityNames,
            analysis = answer,
        });
    }

    // ====================================================================
    // Skill 8: Impact Analysis
    // ====================================================================

    public async Task<string> AnalyzeImpactAsync(string entityName, string targetName)
    {
        var ctx = await GatherContextAsync(query: $"{entityName} {targetName}", entityName: entityName);

        // References to this method/property
        var methodRefs = await QueryGraphAsync<dynamic>(
            new QueryDefinition("SELECT c.label, c.from_id, c.to_id, c.to_method FROM c WHERE c.doc_type = 'edge' AND (c.to_method = @name OR CONTAINS(c.to_id, @name))")
                .WithParameter("@name", targetName));

        var callers = await QueryGraphAsync<dynamic>(
            new QueryDefinition("SELECT c.from_id, c.label FROM c WHERE c.to_method = @name AND c.doc_type = 'edge' AND c.label IN ('sends_to_client', 'sends_to_base', 'sends_to_cell', 'calls_self')")
                .WithParameter("@name", targetName));

        var propRefs = await QueryGraphAsync<dynamic>(
            new QueryDefinition("SELECT c.owner, c.name, c.data_type, c.flags FROM c WHERE c.name = @name AND c.label = 'property' AND c.doc_type = 'vertex'")
                .WithParameter("@name", targetName));

        var clientMethods = await QueryGraphAsync<dynamic>(
            new QueryDefinition("SELECT c.name, c.owner FROM c WHERE c.name = @name AND c.method_type = 'ClientMethod' AND c.doc_type = 'vertex'")
                .WithParameter("@name", targetName));

        var systemPrompt = $"""
            You are performing impact analysis for the Cimmeria project — a Rust reimplementation of
            the Stargate Worlds MMORPG server.

            {BigWorldPreamble}

            Analyze the impact of changing '{targetName}' on '{entityName}'. Consider:
            1. **Direct callers** — what methods call this method/use this property?
            2. **Client impact** — is this visible to the client? Will changes break the client binary?
            3. **Inheritance chain** — do child entities override or depend on this?
            4. **Cross-entity calls** — do other entities send messages to this method?
            5. **Game system impact** — which game systems are affected?
            6. **Cascade risk** — what breaks if this changes? Rate: LOW/MEDIUM/HIGH/CRITICAL

            Be specific about each dependency and whether it's a hard contract (client-visible) or
            soft dependency (internal logic that can be adapted).
            {ResponseFormatInstruction}
            """;

        var refData = JsonConvert.SerializeObject(new
        {
            method_references = methodRefs.Count,
            callers = callers.Select(c => new { from = (string)c.from_id, edge = (string)c.label }),
            property_references = propRefs.Select(p => new { owner = (string)p.owner, name = (string)p.name, type = (string)p.data_type, flags = (string)p.flags }),
            client_visible = clientMethods.Count > 0,
        }, Formatting.Indented);

        var answer = await CallGptAsync(systemPrompt, $"""
            Impact analysis for {entityName}.{targetName}

            References found:
            {refData}

            {ctx.FormatImplContext()}
            {ctx.FormatProtocolContext()}
            {ctx.FormatGraphContext()}

            Related Code:
            {FormatCodeResults(ctx.CodeResults)}
            """);

        return Respond("impact_analysis", new
        {
            entity = entityName, target = targetName,
            reference_count = methodRefs.Count,
            caller_count = callers.Count,
            client_visible = clientMethods.Count > 0,
            analysis = answer,
        });
    }

    // ====================================================================
    // Skill 9: Sequence Tracer
    // ====================================================================

    public async Task<string> TraceSequenceAsync(string scenario)
    {
        var ctx = await GatherContextAsync(query: scenario, codeTopN: 10);

        // Find entities mentioned in the scenario
        var entityNames = new List<string>();
        var allEntities = await QueryGraphAsync<dynamic>(
            new QueryDefinition("SELECT c.name FROM c WHERE c.doc_type = 'vertex' AND c.label IN ('entity', 'interface')"));
        foreach (var e in allEntities)
        {
            if (scenario.Contains((string)e.name, StringComparison.OrdinalIgnoreCase))
                entityNames.Add((string)e.name);
        }

        // Get call chains for involved entities
        var callChains = new List<dynamic>();
        foreach (var eName in entityNames)
        {
            var chains = await QueryGraphAsync<dynamic>(
                new QueryDefinition("SELECT c.label, c.from_id, c.to_id, c.to_method FROM c WHERE CONTAINS(c.from_id, @entity) AND c.doc_type = 'edge' AND c.label IN ('sends_to_client', 'sends_to_base', 'sends_to_cell', 'calls_self')")
                    .WithParameter("@entity", eName));
            callChains.AddRange(chains);
        }

        var systemPrompt = $"""
            You are a game systems analyst tracing message sequences in the Stargate Worlds MMORPG,
            reimplemented in Rust as the Cimmeria project.

            {BigWorldPreamble}

            Trace the complete message sequence for the given scenario. Show:
            1. **Trigger** — what initiates this sequence (client action, timer, game event)
            2. **Step-by-step flow** — each method call in order, with:
               - Source entity and method
               - Direction (Client->Server, Server->Client, Cell->Base, etc.)
               - Data passed (property values, method args)
               - State changes (property updates, flag changes)
            3. **Branching** — conditional paths (success/failure, different states)
            4. **Client updates** — what the player sees at each step
            5. **Timing** — any delays, timers, or async operations

            Format as a numbered sequence with clear arrows showing message direction.
            {ResponseFormatInstruction}
            """;

        var chainData = JsonConvert.SerializeObject(
            callChains.Select(c => new { label = (string)c.label, from = (string)c.from_id, to_method = c.to_method?.ToString() ?? "" }),
            Formatting.Indented);

        var answer = await CallGptAsync(systemPrompt, $"""
            Trace the message sequence for: {scenario}

            Entities involved: {string.Join(", ", entityNames)}
            {ctx.FormatGraphContext()}

            Call chains:
            {chainData}

            Related Code:
            {FormatCodeResults(ctx.CodeResults)}
            """, maxTokens: 4000);

        return Respond("trace_sequence", new
        {
            scenario,
            entities_involved = entityNames,
            call_chain_count = callChains.Count,
            sequence = answer,
        });
    }

    // ====================================================================
    // Skill 10: What's Next (Priority Recommender)
    // ====================================================================

    public async Task<string> SuggestNextAsync()
    {
        var entities = await QueryGraphAsync<dynamic>(
            new QueryDefinition("SELECT c.name, c.property_count, c.client_method_count, c.cell_method_count, c.base_method_count FROM c WHERE c.doc_type = 'vertex' AND c.label = 'entity'"));

        var entityStats = new List<object>();
        foreach (var e in entities)
        {
            string name = (string)e.name;

            var pyCount = await QueryGraphAsync<dynamic>(
                new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.label = 'script_method' AND c.owner = @owner AND c.doc_type = 'vertex'")
                    .WithParameter("@owner", name));

            var cppCount = await QueryGraphAsync<dynamic>(
                new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.label = 'cpp_method' AND c.owner = @owner AND c.doc_type = 'vertex'")
                    .WithParameter("@owner", name));

            var depCount = await QueryGraphAsync<dynamic>(
                new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.to_id = @toId AND c.doc_type = 'edge'")
                    .WithParameter("@toId", $"entity:{name}"));

            entityStats.Add(new
            {
                name,
                total_methods = (int)e.client_method_count + (int)e.cell_method_count + (int)e.base_method_count,
                client_methods = (int)e.client_method_count,
                python_implementations = pyCount.Count > 0 ? pyCount[0] : 0,
                cpp_implementations = cppCount.Count > 0 ? cppCount[0] : 0,
                dependents = depCount.Count > 0 ? depCount[0] : 0,
            });
        }

        var systemPrompt = $"""
            You are a project manager for the Cimmeria project — a Rust reimplementation of the
            Stargate Worlds MMORPG server.

            {BigWorldPreamble}

            Analyze the implementation status and recommend what to implement next.
            Prioritize by:
            1. **Client-critical** — entities with many ClientMethods (client crashes without them)
            2. **Dependency count** — entities that many others depend on should come first
            3. **Python coverage** — entities with good Python scripts have clear behavioral specs
            4. **Game impact** — core gameplay entities (player, combat, inventory) over niche ones

            Produce a ranked list with:
            - Entity name and why it's this priority
            - Estimated complexity (S/M/L/XL)
            - Dependencies that must exist first
            - Which game systems it unlocks
            {ResponseFormatInstruction}
            """;

        var answer = await CallGptAsync(systemPrompt, $"""
            Current implementation status:
            {JsonConvert.SerializeObject(entityStats, Formatting.Indented)}
            """);

        return Respond("whats_next", new
        {
            entity_count = entities.Count,
            status = entityStats,
            recommendations = answer,
        });
    }

    // ====================================================================
    // Skill 11: Compatibility Check
    // ====================================================================

    public async Task<string> CheckCompatibilityAsync(string code, string entityName)
    {
        var ctx = await GatherContextAsync(entityName: entityName, code: code);

        if (ctx.Entity == null)
            return Respond("compatibility_check", new { error = $"Entity '{entityName}' not found." });

        // Also get inherited protocol
        var inheritedProtocol = new List<string>();
        if (ctx.Parent != null)
            inheritedProtocol.Add(await GetProtocolContextAsync(ctx.Parent));
        foreach (var iface in ctx.Interfaces)
            inheritedProtocol.Add(await GetProtocolContextAsync(iface));

        var systemPrompt = $"""
            You are a compatibility verification engine for the Cimmeria project.
            The original SGW game client is a FIXED BINARY that CANNOT be modified.

            {BigWorldPreamble}

            Verify the Rust code against the .def specification. Check EVERY item:

            CRITICAL (client will crash or disconnect):
            - Missing ClientMethods that the client expects to receive
            - Missing exposed BaseMethods that the client tries to call
            - Wrong argument types or order on any client-visible method
            - Missing CELL_PUBLIC properties (client expects to read these)
            - Wrong property types on replicated properties

            WARNING (incorrect behavior but no crash):
            - Missing CellMethods (gameplay won't work but client stays connected)
            - Wrong default values on properties
            - Missing interface implementations

            For each issue found, output:
            - Severity: CRITICAL / WARNING
            - What: the specific method or property
            - Expected: what the .def says
            - Found: what the Rust code has (or "MISSING")
            - Fix: exact code change needed

            End with a compatibility score: X/Y methods correct, X/Y properties correct.
            {ResponseFormatInstruction}
            """;

        var answer = await CallGptAsync(systemPrompt, $"""
            Verify this Rust code for client compatibility:

            ```rust
            {code}
            ```

            Entity Specification (.def):
            {ctx.EntitySpec}

            {ctx.FormatProtocolContext()}

            {(inheritedProtocol.Count > 0 ? "Inherited Protocol:\n" + string.Join("\n\n", inheritedProtocol) : "")}
            """, maxTokens: 4000);

        return Respond("compatibility_check", new
        {
            entity = entityName,
            method_count = ctx.Methods.Count,
            property_count = ctx.Props.Count,
            interfaces = ctx.Interfaces,
            compatibility_report = answer,
        });
    }

    // ====================================================================
    // Skill 12: Test Generator
    // ====================================================================

    public async Task<string> GenerateTestsAsync(string entityName, string? methodName)
    {
        var searchQuery = methodName != null
            ? $"{entityName} {methodName} Python"
            : $"{entityName} Python cell base";

        var ctx = await GatherContextAsync(query: searchQuery, entityName: entityName, codeTopN: 8);

        if (ctx.Entity == null)
            return Respond("generate_tests", new { error = $"Entity '{entityName}' not found." });

        // Get enums used in properties for test data
        var enumContext = new List<string>();
        foreach (var p in ctx.Props)
        {
            var enumResults = await QueryGraphAsync<dynamic>(
                new QueryDefinition("SELECT c.name, c.token_count FROM c WHERE c.name = @name AND c.label = 'enumeration' AND c.doc_type = 'vertex'")
                    .WithParameter("@name", (string)p.data_type));
            foreach (var er in enumResults)
                enumContext.Add($"Enum {er.name}: {er.token_count} values");
        }

        var targetMethods = methodName != null
            ? ctx.Methods.Where(m => (string)m.name == methodName).ToList()
            : ctx.Methods;

        var targetSpec = JsonConvert.SerializeObject(new
        {
            name = entityName, parent = ctx.Parent, interfaces = ctx.Interfaces,
            properties = ctx.Props.Select(p => new { name = (string)p.name, type = (string)p.data_type, flags = (string)p.flags }),
            methods = targetMethods.Select(m => new { name = (string)m.name, type = (string)m.method_type, args = m.args }),
        }, Formatting.Indented);

        var systemPrompt = $"""
            You are a Rust test engineer for the Cimmeria project.

            {BigWorldPreamble}

            Generate comprehensive Rust tests for the specified entity/methods. Include:

            1. **Unit tests** for each method:
               - Happy path with valid inputs
               - Edge cases from Python behavior (boundary values, empty collections, None/null)
               - Error cases (invalid args, wrong state)

            2. **Property tests**:
               - Default values match .def specification
               - Type constraints are enforced
               - CELL_PUBLIC properties serialize correctly

            3. **Protocol tests**:
               - ClientMethod calls produce correct serialized messages
               - BaseMethod handlers accept the right argument types
               - Replicated property updates trigger client notifications

            4. **Integration tests**:
               - State machine transitions (if applicable)
               - Timer callbacks fire correctly
               - Cross-entity interactions

            Use #[test] and #[should_panic] attributes. Use descriptive test names.
            Include test fixtures/helpers where needed. Output compilable Rust test code.
            {ResponseFormatInstruction}
            """;

        var answer = await CallCodexAsync(systemPrompt, $"""
            Generate Rust tests for {entityName}{(methodName != null ? $".{methodName}" : "")}:

            Entity Definition:
            {targetSpec}

            {ctx.FormatProtocolContext()}
            {(enumContext.Count > 0 ? "\nEnums:\n" + string.Join("\n", enumContext) : "")}

            Python Behavioral Reference:
            {FormatCodeResults(ctx.CodeResults)}
            """);

        return Respond("generate_tests", new
        {
            entity = entityName, method = methodName,
            test_code = answer,
            python_sources = ctx.CodeResults.Select(r => new { file = r.path, relevance = Math.Round(1 - r.distance, 3) }),
        }, CodexModel);
    }

    // ====================================================================
    // Skill 13: Diagram Generator
    // ====================================================================

    public async Task<string> GenerateDiagramAsync(string subject, string? diagramType)
    {
        var ctx = await GatherContextAsync(query: subject);

        // Find entities mentioned
        var entityNames = new List<string>();
        var allEntities = await QueryGraphAsync<dynamic>(
            new QueryDefinition("SELECT c.name FROM c WHERE c.doc_type = 'vertex' AND c.label IN ('entity', 'interface', 'game_system')"));
        foreach (var e in allEntities)
        {
            if (subject.Contains((string)e.name, StringComparison.OrdinalIgnoreCase))
                entityNames.Add((string)e.name);
        }

        // Get relationships
        var relationships = new List<dynamic>();
        foreach (var name in entityNames)
        {
            var edges = await QueryGraphAsync<dynamic>(
                new QueryDefinition("SELECT c.label, c.from_id, c.to_id FROM c WHERE (CONTAINS(c.from_id, @name) OR CONTAINS(c.to_id, @name)) AND c.doc_type = 'edge'")
                    .WithParameter("@name", name));
            relationships.AddRange(edges);
        }

        if (entityNames.Count == 0)
        {
            var systemEdges = await QueryGraphAsync<dynamic>(
                new QueryDefinition("SELECT c.label, c.from_id, c.to_id FROM c WHERE c.label IN ('inherits', 'implements', 'part_of_system') AND c.doc_type = 'edge'"));
            relationships.AddRange(systemEdges);
            entityNames = allEntities.Select(e => (string)e.name).ToList();
        }

        var type = diagramType ?? "auto";

        var systemPrompt = $"""
            You are a technical diagram generator for the Cimmeria project.

            Generate a Mermaid diagram for the given subject. Choose the best diagram type:
            - classDiagram: for entity relationships, inheritance, interfaces
            - sequenceDiagram: for message flows between entities
            - flowchart: for game system logic flows
            - stateDiagram-v2: for entity state machines
            - graph: for dependency/relationship maps

            {(type != "auto" ? $"The user requested a {type} diagram specifically." : "Choose the most appropriate type.")}

            Rules:
            - Keep it readable — max ~20 nodes per diagram
            - Use meaningful labels on edges
            - Group related items with subgraphs where helpful
            - Use BigWorld terminology (Base, Cell, Client, RPC, etc.)
            - Output ONLY the Mermaid code block, no explanation
            """;

        var relData = JsonConvert.SerializeObject(
            relationships.Take(100).Select(r => new { label = (string)r.label, from = (string)r.from_id, to = (string)r.to_id }),
            Formatting.Indented);

        var answer = await CallCodexAsync(systemPrompt, $"""
            Generate a Mermaid diagram for: {subject}

            Entities/Systems: {string.Join(", ", entityNames)}

            Relationships:
            {relData}

            {ctx.FormatGraphContext()}
            """);

        return Respond("diagram", new
        {
            subject, diagram_type = type,
            entities = entityNames,
            mermaid = answer,
        }, CodexModel);
    }

    // ====================================================================
    // Skill 14: Game Design Decoder
    // ====================================================================

    public async Task<string> DecodeGameDesignAsync(string systemOrFeature)
    {
        var ctx = await GatherContextAsync(query: systemOrFeature, codeTopN: 10);

        // Game defs related to this feature
        var gameDefs = await QueryGraphAsync<dynamic>(
            new QueryDefinition("SELECT c.name, c.field_count FROM c WHERE c.label = 'game_def' AND CONTAINS(LOWER(c.name), LOWER(@name)) AND c.doc_type = 'vertex'")
                .WithParameter("@name", systemOrFeature));

        // Related enums
        var enums = await QueryGraphAsync<dynamic>(
            new QueryDefinition("SELECT c.name, c.token_count FROM c WHERE c.label = 'enumeration' AND CONTAINS(LOWER(c.name), LOWER(@name)) AND c.doc_type = 'vertex'")
                .WithParameter("@name", systemOrFeature));

        // Match game systems
        var systems = await QueryGraphAsync<dynamic>(
            new QueryDefinition("SELECT c.name, c.description, c.keywords FROM c WHERE c.label = 'game_system' AND c.doc_type = 'vertex'"));
        var matchedSystems = new List<dynamic>();
        foreach (var s in systems)
        {
            string name = (string)s.name;
            string desc = (string)s.description;
            if (systemOrFeature.Contains(name, StringComparison.OrdinalIgnoreCase) ||
                name.Contains(systemOrFeature, StringComparison.OrdinalIgnoreCase) ||
                desc.Contains(systemOrFeature, StringComparison.OrdinalIgnoreCase))
            {
                matchedSystems.Add(s);
            }
        }

        var systemPrompt = $"""
            You are a game designer reverse-engineering the design of Stargate Worlds (SGW) MMORPG
            from its server codebase.

            {BigWorldPreamble}

            Reconstruct the PLAYER EXPERIENCE of this game feature/system. Explain:
            1. **What the player sees** — UI elements, feedback, animations they'd experience
            2. **How it works** — the gameplay loop from the player's perspective
            3. **Rules & mechanics** — damage formulas, cooldowns, caps, costs, requirements
            4. **Progression** — how this system evolves as the player levels up
            5. **Interactions** — how this system connects to other systems (combat <-> abilities <-> items)
            6. **Data model** — what game data definitions drive this system (items, abilities, missions, etc.)
            7. **Interesting design choices** — anything unusual or clever in the design

            Write for a game designer audience, not a programmer. Translate code concepts into
            gameplay concepts. Reference specific enums, constants, and data values to back up claims.
            {ResponseFormatInstruction}
            """;

        var answer = await CallGptAsync(systemPrompt, $"""
            Decode the game design for: {systemOrFeature}

            {(matchedSystems.Count > 0 ? "Game Systems:\n" + JsonConvert.SerializeObject(matchedSystems, Formatting.Indented) : "")}
            {(gameDefs.Count > 0 ? "\nGame Definitions:\n" + JsonConvert.SerializeObject(gameDefs.Select(g => new { name = (string)g.name, fields = (int)g.field_count }), Formatting.Indented) : "")}
            {(enums.Count > 0 ? "\nEnumerations:\n" + JsonConvert.SerializeObject(enums.Select(e => new { name = (string)e.name, values = (int)e.token_count }), Formatting.Indented) : "")}
            {ctx.FormatGraphContext()}

            Source Code:
            {FormatCodeResults(ctx.CodeResults)}
            """);

        return Respond("game_design", new
        {
            feature = systemOrFeature,
            game_systems = matchedSystems.Select(s => (string)s.name),
            game_definitions = gameDefs.Select(g => (string)g.name),
            enumerations = enums.Select(e => (string)e.name),
            design_analysis = answer,
        });
    }
}
