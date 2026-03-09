# Cimmeria MCP Server

A hosted MCP (Model Context Protocol) server that provides semantic search over the [Cimmeria](https://github.com/SandboxServers/Cimmeria) Stargate Worlds server codebase. Built as a C# Azure Function App using the Azure Functions MCP Extension, served over Streamable HTTP — collaborators connect with just a URL, no keys or local setup required.

## How It Works

The Cimmeria codebase is indexed into Azure AI Search (`cimmeria-code` index) with `text-embedding-3-small` embeddings. This Function App wraps that index as an MCP server, enabling AI assistants like Claude to search, browse, and understand the codebase through natural language queries.

```
Claude Code ──HTTP──▸ Azure Functions ──▸ Azure OpenAI (embed query)
                      /runtime/webhooks/mcp    │
                                               ▼
                                         Azure AI Search
                                         (hybrid text + vector)
```

## MCP Tools

| Tool | Description |
|------|-------------|
| `search_cimmeria` | Semantic search across the entire codebase (hybrid text + vector) |
| `list_cimmeria_files` | List all indexed files, optionally filtered by extension |
| `get_file_content` | Retrieve full file content by reassembling chunks |
| `find_similar_code` | Find similar code patterns given a snippet |
| `get_project_overview` | File counts by type, directory structure, index stats |
| `search_by_directory` | Semantic search scoped to a specific directory path |

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Azure Functions Core Tools](https://learn.microsoft.com/azure/azure-functions/functions-run-local) v4.0.7030+
- Azure AI Search index (`cimmeria-code`) already populated
- Azure OpenAI deployment with `text-embedding-3-small`

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
       "SEARCH_KEY": "your-key"
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
│   ├── Program.cs                  # Host builder + DI
│   ├── Tools/
│   │   └── CimmeriaSearchTools.cs  # 6 MCP tool functions
│   ├── Services/
│   │   └── CimmeriaSearchService.cs # Embedding + search logic
│   └── host.json                   # MCP extension config
├── infra/                          # Terraform (Function App + plan)
├── pipelines/                      # Azure Pipelines (build/test/deploy)
└── scripts/
    └── Deploy-Local.ps1            # Local publish + deploy
```

## Tech Stack

- **.NET 10** isolated worker, Azure Functions v4
- **Azure Functions MCP Extension** (`Microsoft.Azure.Functions.Worker.Extensions.Mcp`)
- **Azure AI Search** with hybrid (text + vector) queries
- **Azure OpenAI** for query embeddings (`text-embedding-3-small`)
- **Terraform** for infrastructure
- **Azure Pipelines** for CI/CD
