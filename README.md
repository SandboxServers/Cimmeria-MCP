# Cimmeria MCP Server

A hosted MCP (Model Context Protocol) server providing AI-powered codebase intelligence for the [Cimmeria](https://github.com/SandboxServers/Cimmeria) Stargate Worlds server emulator, SGW client assets, and BigWorld engine. Built as a C# Azure Function App using the Azure Functions MCP Extension, served over Streamable HTTP.

## How It Works

The codebase is indexed across two stores: **Azure AI Search** (`cimmeria-code` index) for semantic code search, and **Cosmos DB** (`knowledge-graph` container) for a structured knowledge graph of entities, methods, properties, enums, types, game definitions, and implementation coverage. AI skills combine both data sources with **GPT-5.4** to provide synthesized answers, code generation, and analysis.

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
                                     │    │  Graph)      │
                                     │    └──────────────┘
                                     │    ┌──────────────┐
                                     └───▸│ GPT-5.4      │
                                          │ (AI Skills)  │
                                          └──────────────┘
```

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
- Azure OpenAI deployments: `text-embedding-3-small`, `gpt-5.4`
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

2. Build and run:
   ```bash
   dotnet build src/CimmeriaMcp.Functions
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

## Deployment

### Infrastructure (Terraform)

```bash
cd infra
terraform init
terraform plan -var-file=terraform.tfvars
terraform apply -var-file=terraform.tfvars
```

Creates a Windows Consumption plan (Y1) + Function App in the `ailab-rg` resource group.

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
│   │   ├── CimmeriaSearchService.cs       # Embedding + AI Search logic
│   │   ├── CimmeriaGraphService.cs        # Cosmos DB knowledge graph queries
│   │   └── CimmeriaSummarizationService.cs # GPT-5.4 AI skills engine
│   └── host.json                          # MCP extension config
├── infra/                                 # Terraform (Function App + plan)
├── pipelines/                             # Azure Pipelines (build/test/deploy)
└── scripts/
    └── Deploy-Local.ps1                   # Local publish + deploy
```

## Tech Stack

- **.NET 10** isolated worker, Azure Functions v4
- **Azure Functions MCP Extension** (`Microsoft.Azure.Functions.Worker.Extensions.Mcp`)
- **Azure AI Search** with hybrid (text + vector) queries
- **Azure OpenAI** — `text-embedding-3-small` (embeddings), `gpt-5.4` (AI skills)
- **Cosmos DB** — NoSQL knowledge graph (4,801 vertices, 4,340 edges)
- **Terraform** for infrastructure
- **Azure Pipelines** for CI/CD
