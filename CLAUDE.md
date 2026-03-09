# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Cimmeria MCP Server — a C# Azure Function App that serves RAG search over the Cimmeria (Stargate Worlds) server codebase and the SGW game client assets via MCP (Model Context Protocol) over Streamable HTTP. Code chunks with `text-embedding-3-small` embeddings are stored in Azure Cosmos DB NoSQL with built-in vector search.

Uses the **Azure Functions MCP Extension** (`Microsoft.Azure.Functions.Worker.Extensions.Mcp`) — transport, auth, and MCP protocol are handled by the Functions runtime at `/runtime/webhooks/mcp`. Auth uses a system key.

## Build Commands

```bash
# Build
dotnet build src/CimmeriaMcp.Functions

# Restore only
dotnet restore src/CimmeriaMcp.Functions

# Publish (release)
dotnet publish src/CimmeriaMcp.Functions -c Release -o ./publish

# Run locally (requires Azure Functions Core Tools)
cd src/CimmeriaMcp.Functions && func start

# Deploy to Azure (PowerShell)
./scripts/Deploy-Local.ps1 -AppName cimmeria-mcp
```

## Architecture

**Runtime**: .NET 10 isolated worker, Azure Functions v4, Windows Consumption plan (Y1).

**Data store**: Azure Cosmos DB NoSQL (`cimmeria-cosmos` account, free tier). Database `cimmeria`, container `code-chunks` with partition key `/source_project` and flat vector index on `/embedding`.

**Sources indexed**:
- `cimmeria-server` — Cimmeria Stargate Worlds server (C++, Rust, Python, SQL, docs)
- `sgw-client` — SGW game client assets (Lua UI scripts, XML layouts, INI configs, shaders, localization)

### Key Components

- **`Tools/CimmeriaSearchTools.cs`** — All 6 MCP tool functions. Each uses `[McpToolTrigger]` and `[McpToolProperty]` attributes. Functions are thin wrappers that delegate to the search service. All tools accept an optional `source` filter (`cimmeria-server` or `sgw-client`).
- **`Services/CimmeriaSearchService.cs`** — Core logic: embeds queries via Azure OpenAI `EmbeddingClient`, performs vector search against Cosmos DB using `VectorDistance()` SQL function. Registered as singleton via DI.
- **`Program.cs`** — Host builder, DI registration.
- **`host.json`** — MCP extension config (`serverName`, `serverVersion`, `instructions`). Extension bundle `[4.0.0, 5.0.0)`.

### MCP Tools (6 total)

| Tool | Purpose |
|------|---------|
| `search_cimmeria` | Vector semantic search (main tool) |
| `list_cimmeria_files` | List all indexed files, optional type/source filter |
| `get_file_content` | Reassemble full file from chunks by `file_path` |
| `find_similar_code` | Pure vector search for similar code patterns |
| `get_project_overview` | File type counts, directory tree, index stats |
| `search_by_directory` | Semantic search scoped to a path prefix |

### MCP Extension API Conventions

- `[McpToolProperty]` takes positional args `(string propertyName, string description)` and a named parameter `isRequired: true` for required properties. Optional properties omit `isRequired`.
- The `Microsoft.Azure.Functions.Worker.Extensions.Mcp` package is prerelease — pin to `1.2.0-preview.1` or later. It requires `Microsoft.Azure.Functions.Worker >= 2.51.0`.

### Cosmos DB Notes

- Uses `Microsoft.Azure.Cosmos` v3.46.0 with System.Text.Json (Newtonsoft check disabled via `AzureCosmosDisableNewtonsoftJsonCheck`).
- Vector search uses `VectorDistance()` with cosine distance (lower = more similar).
- Container uses `flat` vector index type (appropriate for < 10K documents).
- Free tier: 1000 RU/s, 25 GB. The indexer rate-limits writes to ~400 RU/s.

### Infrastructure (`infra/`)

Terraform configs targeting existing `ailab-rg` resource group. Creates Service Plan (Y1) + Windows Function App. Uses existing storage account `ailabstoragesc`. App settings inject `OPENAI_ENDPOINT`, `OPENAI_KEY`, `COSMOS_ENDPOINT`, `COSMOS_KEY`.

**Important**: Do NOT set `FUNCTIONS_WORKER_RUNTIME` in `app_settings` when using `application_stack` in Terraform — they conflict. `always_on` must be `false` on Consumption plan.

### Pipelines (`pipelines/`)

Azure Pipelines with template hierarchy: `azure-pipelines.yml` → stage template → job templates → step templates. Three stages: Build, Test, Deploy (deploy only on `main`).

## Environment Variables

Required in `local.settings.json` for local dev (gitignored):

| Variable | Purpose |
|----------|---------|
| `OPENAI_ENDPOINT` | Azure OpenAI endpoint URL |
| `OPENAI_KEY` | Azure OpenAI API key |
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
