# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Cimmeria MCP Server — a C# Azure Function App providing AI-powered codebase intelligence for the Cimmeria (Stargate Worlds) server emulator, SGW client assets, and BigWorld engine via MCP (Model Context Protocol) over Streamable HTTP. 34 tools across RAG search, knowledge graph queries, and GPT-5.4 AI skills.

Uses the **Azure Functions MCP Extension** (`Microsoft.Azure.Functions.Worker.Extensions.Mcp`) — transport, auth, and MCP protocol are handled by the Functions runtime at `/runtime/webhooks/mcp`. Auth uses a system key.

## Build Commands

```bash
# Build
dotnet build

# Test
dotnet test

# Publish (release)
dotnet publish src/CimmeriaMcp.Functions -c Release -o ./publish

# Run locally (requires Azure Functions Core Tools)
cd src/CimmeriaMcp.Functions && func start

# Deploy to Azure (PowerShell)
./scripts/Deploy-Local.ps1 -AppName cimmeria-mcp
```

## Architecture

**Runtime**: .NET 10 isolated worker, Azure Functions v4, Windows Consumption plan (Y1).

### Data Stores

- **Azure AI Search** (`cimmeria-code` index) — hybrid text + HNSW vector search for `cimmeria-server` source, 505-dim `text-embedding-3-small` embeddings, cosine similarity. Populated by Cosmos DB indexer (5-min schedule).
- **Cosmos DB NoSQL** (`cimmeria` database) — two containers:
  - `code-chunks` — embedded code snippets with vector index, partitioned by `/source_project`. Used as vector search fallback for `sgw-client` and `bigworld-engine` sources.
  - `knowledge-graph` — 4,801 vertices + 4,340 edges (entities, methods, properties, enums, types, game defs, C++ classes, worlds), partitioned by `/pk`
- **Azure OpenAI** — `text-embedding-3-small` (embeddings), `gpt-5-4` deployment (AI skills)

### Key Components

- **`Tools/CimmeriaSearchTools.cs`** — 6 RAG search tools. Thin wrappers delegating to `CimmeriaSearchService`.
- **`Tools/CimmeriaGraphTools.cs`** — 14 knowledge graph tools. Thin wrappers delegating to `CimmeriaGraphService`.
- **`Tools/CimmeriaAiTools.cs`** — 14 AI skill tools. Thin wrappers delegating to `CimmeriaSummarizationService`.
- **`Services/CimmeriaSearchService.cs`** — Routes `cimmeria-server` queries to Azure AI Search (hybrid text + vector), falls back to Cosmos DB `VectorDistance()` for other sources. Internal constructor for testing.
- **`Services/CimmeriaGraphService.cs`** — Queries Cosmos DB knowledge graph. Uses `Newtonsoft.Json.JsonConvert.SerializeObject()` (not System.Text.Json) because Cosmos SDK v3 returns `JObject` for dynamic queries.
- **`Services/CimmeriaSummarizationService.cs`** — GPT-5.4 AI skills engine. Shared helpers: `GatherContextAsync()` (context-aware input gathering), `SearchCodeAsync()` (RAG), `GetEntityContextAsync()` (graph), `CallGptAsync()` (unified GPT call). Standardized response format via `ResponseFormatInstruction` constant and `Respond()` wrapper.
- **`Program.cs`** — Host builder, DI registration (3 singleton services).
- **`host.json`** — MCP extension config (`serverName`, `serverVersion`, `instructions`). Extension bundle `[4.0.0, 5.0.0)`.

### MCP Tools (34 total)

**Search (6)**: `search_cimmeria`, `list_cimmeria_files`, `get_file_content`, `find_similar_code`, `get_project_overview`, `search_by_directory`

**Knowledge Graph (14)**: `get_entity_details`, `get_inheritance_tree`, `get_graph_overview`, `get_game_system_details`, `get_replicated_properties`, `get_method_call_chain`, `traverse_graph`, `lookup_enum`, `resolve_type`, `lookup_game_def`, `get_implementation_status`, `cross_reference`, `get_entity_protocol`, `lookup_bigworld_api`

**AI Skills (14)**: `explain_cimmeria`, `generate_entity_stub`, `translate_python_to_rust`, `generate_tests`, `troubleshoot`, `review_code`, `check_compatibility`, `analyze_impact`, `plan_implementation`, `whats_next`, `analyze_protocol`, `trace_sequence`, `generate_diagram`, `decode_game_design`

### MCP Extension API Conventions

- `[McpToolProperty]` takes positional args `(string propertyName, string description)` and a named parameter `isRequired: true` for required properties. Optional properties omit `isRequired`.
- The `Microsoft.Azure.Functions.Worker.Extensions.Mcp` package is prerelease — pin to `1.2.0-preview.1` or later. It requires `Microsoft.Azure.Functions.Worker >= 2.51.0`.

### Cosmos DB Notes

- Knowledge graph uses `doc_type` field (`vertex` or `edge`) with snake_case field names (`from_id`, `to_id`, `method_type`, `data_type`).
- `c.value` is a reserved word in Cosmos DB SQL — must use `c["value"]` to escape.
- Cosmos SDK v3 dynamic queries return `Newtonsoft.Json.Linq.JObject` — `System.Text.Json.JsonSerializer` cannot serialize these. Always use `JsonConvert.SerializeObject()`.
- Document IDs cannot contain `/`, `\`, `#`, `?` — use `safe_id()` to sanitize.

### Infrastructure (`infra/`)

Terraform and Bicep templates manage ALL Azure resources: Cosmos DB (account, database, 2 containers), Azure OpenAI (account + 5 model deployments), Azure AI Search, Service Plan (Y1), Function App. App settings are derived from resource references — no manual secret injection.

Key variables: `create_resource_group` (bool, for test deployments), `deploy_search` (bool, skip AI Search in tests — free tier limited to 1/sub), `cosmos_free_tier` (bool).

**Important**: Do NOT set `FUNCTIONS_WORKER_RUNTIME` in `app_settings` when using `application_stack` in Terraform — they conflict. `always_on` must be `false` on Consumption plan.

### Testing (`src/CimmeriaMcp.Functions.Tests/`)

xUnit test project with structural tests: search routing, method signatures, GPT deployment name, response format, AI skill completeness. Run with `dotnet test`.

### Pipelines (`pipelines/`)

Azure Pipelines with template hierarchy: `azure-pipelines.yml` -> stage template -> job templates -> step templates. Three stages: Build, Test, Deploy (deploy only on `main`).

## Environment Variables

Required in `local.settings.json` for local dev (gitignored):

| Variable | Purpose |
|----------|---------|
| `OPENAI_ENDPOINT` | Azure OpenAI endpoint URL |
| `OPENAI_KEY` | Azure OpenAI API key |
| `SEARCH_ENDPOINT` | Azure AI Search endpoint URL |
| `SEARCH_KEY` | Azure AI Search API key |
| `COSMOS_ENDPOINT` | Azure Cosmos DB endpoint URL |
| `COSMOS_KEY` | Azure Cosmos DB primary key |

## Client Connection

Remote:
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

Local dev (no auth needed):
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
