# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Cimmeria MCP Server — a C# Azure Function App that serves RAG search over the Cimmeria (Stargate Worlds) codebase via MCP (Model Context Protocol) over Streamable HTTP. The codebase is indexed in Azure AI Search (`cimmeria-code` index) with `text-embedding-3-small` embeddings.

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

### Key Components

- **`Tools/CimmeriaSearchTools.cs`** — All 6 MCP tool functions. Each uses `[McpToolTrigger]` and `[McpToolProperty]` attributes. Functions are thin wrappers that delegate to the search service.
- **`Services/CimmeriaSearchService.cs`** — Core logic: embeds queries via Azure OpenAI `EmbeddingClient`, performs hybrid (text + vector) search against Azure AI Search. Registered as singleton via DI.
- **`Program.cs`** — Host builder, DI registration.
- **`host.json`** — MCP extension config (`serverName`, `serverVersion`, `instructions`). Extension bundle `[4.0.0, 5.0.0)`.

### MCP Tools (6 total)

| Tool | Purpose |
|------|---------|
| `search_cimmeria` | Hybrid semantic search (main tool) |
| `list_cimmeria_files` | List all indexed files, optional type filter |
| `get_file_content` | Reassemble full file from chunks by `file_path` |
| `find_similar_code` | Pure vector search for similar code patterns |
| `get_project_overview` | File type counts, directory tree, index stats |
| `search_by_directory` | Semantic search scoped to a path prefix |

### MCP Extension API Conventions

- `[McpToolProperty]` takes positional args `(string propertyName, string description)` and a named parameter `isRequired: true` for required properties. Optional properties omit `isRequired`.
- The `Microsoft.Azure.Functions.Worker.Extensions.Mcp` package is prerelease — pin to `1.2.0-preview.1` or later. It requires `Microsoft.Azure.Functions.Worker >= 2.51.0`.

### Infrastructure (`infra/`)

Terraform configs targeting existing `ailab-rg` resource group. Creates Service Plan (Y1) + Windows Function App. Uses existing storage account `ailabstoragesc`. App settings inject `OPENAI_ENDPOINT`, `OPENAI_KEY`, `SEARCH_ENDPOINT`, `SEARCH_KEY`.

**Important**: Do NOT set `FUNCTIONS_WORKER_RUNTIME` in `app_settings` when using `application_stack` in Terraform — they conflict. `always_on` must be `false` on Consumption plan.

### Pipelines (`pipelines/`)

Azure Pipelines with template hierarchy: `azure-pipelines.yml` → stage template → job templates → step templates. Three stages: Build, Test, Deploy (deploy only on `main`).

## Environment Variables

Required in `local.settings.json` for local dev (gitignored):

| Variable | Purpose |
|----------|---------|
| `OPENAI_ENDPOINT` | Azure OpenAI endpoint URL |
| `OPENAI_KEY` | Azure OpenAI API key |
| `SEARCH_ENDPOINT` | Azure AI Search endpoint URL |
| `SEARCH_KEY` | Azure AI Search API key |

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
