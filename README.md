# Cimmeria MCP Server

A hosted MCP (Model Context Protocol) server providing AI-powered codebase intelligence for the [Cimmeria](https://github.com/SandboxServers/Cimmeria) Stargate Worlds server emulator, SGW client assets, and BigWorld engine. Built as a C# Azure Function App using the Azure Functions MCP Extension, served over Streamable HTTP.

## How It Works

The codebase is indexed across two stores: **Azure AI Search** (`cimmeria-code` index) for hybrid text + vector search over cimmeria-server code, and **Cosmos DB** (`knowledge-graph` container) for a structured knowledge graph of entities, methods, properties, enums, types, game definitions, and implementation coverage. The `code-chunks` container provides Cosmos DB vector search as a fallback for non-server sources (sgw-client, bigworld-engine). AI skills combine both data sources with **GPT-5.4** to provide synthesized answers, code generation, and analysis.

```
                                          ┌──────────────┐
                                     ┌───▸│ Azure OpenAI │
                                     │    │  Embeddings  │
                                     │    └──────────────┘
Claude Code ──HTTP──▸ Azure Functions─┤    ┌──────────────┐
                     /runtime/        │───▸│ Azure AI     │
                     webhooks/mcp     │    │ Search (RAG) │
                                     │    └──────────────┘
                                     │    ┌──────────────┐
                                     │───▸│ Cosmos DB    │
                                     │    │ (Knowledge   │
                                     │    │  Graph +     │
                                     │    │  Fallback)   │
                                     │    └──────────────┘
                                     │    ┌──────────────┐
                                     └───▸│ GPT-5.4      │
                                          │ (AI Skills)  │
                                          └──────────────┘
```

### Search Architecture

| Source | Primary Search | Fallback |
|--------|---------------|----------|
| `cimmeria-server` | Azure AI Search (hybrid text + vector) | Cosmos DB `VectorDistance()` |
| `sgw-client` | Cosmos DB `VectorDistance()` | — |
| `bigworld-engine` | Cosmos DB `VectorDistance()` | — |

The AI Search index is populated by a Cosmos DB indexer on a 5-minute schedule, pulling from the `code-chunks` container filtered to `source_project = 'cimmeria-server'`. Hybrid search combines BM25 text ranking with HNSW vector similarity for better results.

## MCP Tools (34 total)

### Search Tools (6)

| Tool | Description |
|------|-------------|
| `search_cimmeria` | Semantic search across the entire codebase (hybrid text + vector) |
| `list_cimmeria_files` | List all indexed files, optionally filtered by extension |
| `get_file_content` | Retrieve full file content by reassembling chunks |
| `find_similar_code` | Find similar code patterns given a snippet |
| `get_project_overview` | File counts by type, directory structure, index stats |
| `search_by_directory` | Semantic search scoped to a specific directory path |

### Knowledge Graph Tools (14)

| Tool | Description |
|------|-------------|
| `get_entity_details` | Full entity/interface details — properties, methods, inheritance |
| `get_inheritance_tree` | Full inheritance hierarchy — ancestors, descendants, interfaces |
| `get_graph_overview` | Knowledge graph overview — vertex/edge counts, all entities/interfaces/systems |
| `get_game_system_details` | Game system details and associated entities |
| `get_replicated_properties` | All CELL_PUBLIC properties including inherited chain |
| `get_method_call_chain` | Trace method call chains — what calls what |
| `traverse_graph` | Walk the graph following a specific edge type |
| `lookup_enum` | Search enumerations by name or token (129 enums + 1,276 constants) |
| `resolve_type` | Resolve BigWorld type aliases — simple, ARRAY, FIXED_DICT (63 types) |
| `lookup_game_def` | Game data definition schemas — fields, cross-references (43 defs) |
| `get_implementation_status` | .def vs Python vs C++ implementation coverage |
| `cross_reference` | Unified search across all graph data |
| `get_entity_protocol` | Full client-server protocol map — RPCs, replicated properties |
| `lookup_bigworld_api` | BigWorld engine API usage and C++ reimplementations |

### AI Skills (14) — powered by GPT-5.4

| Tool | Description |
|------|-------------|
| `explain_cimmeria` | RAG + knowledge graph + GPT synthesized answers to codebase questions |
| `generate_entity_stub` | Generate Rust struct/impl stubs from entity .def definitions |
| `translate_python_to_rust` | Convert Python BigWorld entity scripts to idiomatic Rust |
| `generate_tests` | Generate Rust `#[test]` functions from .def contracts and Python behavior |
| `troubleshoot` | Diagnose issues — protocol mismatches, implementation gaps, type errors |
| `review_code` | Review Rust code against .def specs and Python originals (CRITICAL/WARNING/INFO) |
| `check_compatibility` | Verify Rust code against the fixed SGW client binary — compatibility score |
| `analyze_impact` | Trace all dependents of a method/property change — cascade risk rating |
| `plan_implementation` | Full Rust implementation plan for an entity — priority-ordered with dependencies |
| `whats_next` | Recommend what to implement next based on coverage, dependencies, and game impact |
| `analyze_protocol` | Client-server protocol analysis for an entity or game system |
| `trace_sequence` | Trace complete message sequences for game scenarios (e.g. "player loots a mob") |
| `generate_diagram` | Generate Mermaid diagrams — class, sequence, flowchart, state, dependency |
| `decode_game_design` | Reverse-engineer game design from code — player experience, mechanics, progression |

All AI skills use a standardized response format:
- **Summary** — 1-3 sentence high-level answer
- **Details** — Detailed analysis with clear subheadings
- **Sources & Evidence** — File paths, method names, graph data
- **Confidence** — HIGH / MEDIUM / LOW with explanation

### Knowledge Graph Stats

| Vertex Type | Count |
|-------------|-------|
| Constants | 1,276 |
| Script methods | 856 |
| .def methods | 815 |
| C++ methods | 595 |
| Properties | 436 |
| Source files | 269 |
| C++ classes | 167 |
| Enumerations | 129 |
| Script classes | 81 |
| Type aliases | 63 |
| Game definitions | 43 |
| Worlds | 24 |
| Interfaces | 18 |
| Entities | 17 |
| Game systems | 12 |
| **Total** | **4,801 vertices, 4,340 edges** |

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Azure Functions Core Tools](https://learn.microsoft.com/azure/azure-functions/functions-run-local) v4.0.7030+
- Azure AI Search index (`cimmeria-code`) populated
- Azure OpenAI deployments: `text-embedding-3-small`, `gpt-5-4`
- Cosmos DB with `cimmeria` database, `code-chunks` and `knowledge-graph` containers

### Local Development

1. Copy `local.settings.json` and fill in your keys:
   ```json
   {
     "Values": {
       "AzureWebJobsStorage": "UseDevelopmentStorage=true",
       "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
       "OPENAI_ENDPOINT": "https://your-openai.openai.azure.com/",
       "OPENAI_KEY": "your-key",
       "SEARCH_ENDPOINT": "https://your-search.search.windows.net",
       "SEARCH_KEY": "your-key",
       "COSMOS_ENDPOINT": "https://your-cosmos.documents.azure.com:443/",
       "COSMOS_KEY": "your-key"
     }
   }
   ```

2. Build, test, and run:
   ```bash
   dotnet build
   dotnet test
   cd src/CimmeriaMcp.Functions && func start
   ```

3. MCP endpoint available at `http://localhost:7071/runtime/webhooks/mcp`

### Connecting Claude Code

Remote (deployed):
```json
{
  "mcpServers": {
    "cimmeria-rag": {
      "type": "http",
      "url": "https://<app-name>.azurewebsites.net/runtime/webhooks/mcp",
      "headers": { "x-functions-key": "<mcp_extension system key>" }
    }
  }
}
```

Local dev:
```json
{
  "mcpServers": {
    "cimmeria-rag-local": {
      "type": "http",
      "url": "http://localhost:7071/runtime/webhooks/mcp"
    }
  }
}
```

## Testing

```bash
# Run all tests
dotnet test

# Run with verbose output
dotnet test --verbosity normal
```

The test project (`CimmeriaMcp.Functions.Tests`) contains structural and contract tests for the search service routing logic, GPT deployment configuration, response format compliance, and AI skill method completeness.

## Deployment

### Infrastructure

Both Terraform and Bicep templates manage the complete Azure infrastructure:

```bash
# Terraform
cd infra
terraform init
terraform plan
terraform apply

# Bicep
cd infra/bicep
az deployment group create --resource-group ailab-rg --template-file main.bicep --parameters main.bicepparam
```

Managed resources: Cosmos DB (account, database, 2 containers), Azure OpenAI (account, 5 model deployments), Azure AI Search, App Service Plan, Function App. App settings are derived from resource references — no manual secret injection needed.

### Publish

```powershell
./scripts/Deploy-Local.ps1 -AppName cimmeria-mcp
```

Or via Azure Pipelines (triggers on push to `main`).

## Project Structure

```
├── src/CimmeriaMcp.Functions/
│   ├── Program.cs                         # Host builder + DI
│   ├── Tools/
│   │   ├── CimmeriaSearchTools.cs         # 6 RAG search tools
│   │   ├── CimmeriaGraphTools.cs          # 14 knowledge graph tools
│   │   └── CimmeriaAiTools.cs             # 14 AI skill tools
│   ├── Services/
│   │   ├── CimmeriaSearchService.cs       # AI Search hybrid + Cosmos DB fallback
│   │   ├── CimmeriaGraphService.cs        # Cosmos DB knowledge graph queries
│   │   └── CimmeriaSummarizationService.cs # GPT-5.4 AI skills engine
│   └── host.json                          # MCP extension config
├── src/CimmeriaMcp.Functions.Tests/
│   ├── CimmeriaSearchServiceTests.cs      # Search routing + structure tests
│   └── CimmeriaSummarizationServiceTests.cs # AI skill contract tests
├── infra/
│   ├── main.tf                            # Terraform — all Azure resources
│   ├── variables.tf                       # Resource names + flags
│   ├── outputs.tf                         # Endpoints
│   ├── providers.tf                       # azurerm ~> 4.0
│   ├── tests/deploy.tftest.hcl            # Terraform native test
│   └── bicep/
│       ├── main.bicep                     # Equivalent Bicep template
│       ├── main.bicepparam                # Production parameters
│       └── tests/                         # Bicep test parameters + validation
├── pipelines/                             # Azure Pipelines (build/test/deploy)
└── scripts/
    └── Deploy-Local.ps1                   # Local publish + deploy
```

## Tech Stack

- **.NET 10** isolated worker, Azure Functions v4
- **Azure Functions MCP Extension** (`Microsoft.Azure.Functions.Worker.Extensions.Mcp`)
- **Azure AI Search** — hybrid text + vector (HNSW, 505-dim, cosine) for cimmeria-server
- **Azure OpenAI** — `text-embedding-3-small` (embeddings), `gpt-5-4` (AI skills)
- **Cosmos DB** — NoSQL knowledge graph (4,801 vertices, 4,340 edges) + vector search fallback
- **Terraform** + **Bicep** for infrastructure
- **xUnit** for testing
- **Azure Pipelines** for CI/CD
