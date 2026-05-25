# Cimmeria-MCP colo deployment

How to take Cimmeria-MCP off Azure entirely (except for the actual
OpenAI inference service) and run it from the same docker compose
project that hosts the Cimmeria game server and the SigNoz
observability stack.

The deployment artifacts live in [`docker/`](../docker/):

- [`compose.yml`](../docker/compose.yml) — two services (`cimmeria-mcp`
  + `postgres`), one external network reference (the SigNoz stack on
  the same host), one named volume (Postgres data).
- [`Dockerfile`](../docker/Dockerfile) — multi-stage ASP.NET Core 10
  build. **Speculative** until the C# refactor below lands.
- [`postgres-init/01_schema.sql`](../docker/postgres-init/01_schema.sql) —
  pgvector schema that replaces Cosmos `code-chunks`,
  `knowledge-graph`, and the Azure AI Search index in one DB.
- [`.env.example`](../docker/.env.example) — env-var contract.

## What survives, what gets replaced

| Azure resource | Replacement | Where it runs |
|---|---|---|
| Azure Functions runtime | Plain ASP.NET Core + Kestrel | In the `cimmeria-mcp` container |
| MCP extension (`Microsoft.Azure.Functions.Worker.Extensions.Mcp`) | Official `ModelContextProtocol` SDK (or hand-rolled MCP-over-HTTP) | Same container |
| Azure AI Search `cimmeria-code` index | pgvector + pg_trgm in Postgres | `postgres` container |
| Cosmos DB `code-chunks` container | `code_chunks` table | Same Postgres |
| Cosmos DB `knowledge-graph` container | `kg_vertices` + `kg_edges` tables | Same Postgres |
| Cosmos DB `leases` container | `mcp_indexer_state` table + scheduled job | Same Postgres |
| Azure SignalR Service | In-process `Microsoft.AspNetCore.SignalR.Hub` | Same container |
| Application Insights + Azure Monitor | SigNoz (OTLP) | Existing Cimmeria stack |
| `DefaultAzureCredential` | API-key env vars | — |
| **Azure OpenAI** | **kept** — called out from the colo over the internet | Azure (unchanged) |

The Azure OpenAI service is the only piece that stays in the cloud,
because (a) the colo box has no GPU and (b) Cimmeria-MCP's analytical
tools depend on GPT-5.1-class models. Calling out from the colo to
Azure OpenAI is a single HTTPS dependency — auth is a key, no
managed-identity gymnastics.

## Deployment flow

```text
┌───────────────────────────────────────────────────────────────┐
│ Colo Docker host                                              │
│                                                               │
│  ┌──────────────────────┐    ┌──────────────────────┐         │
│  │ Cimmeria project     │    │ Cimmeria-MCP project │         │
│  │ (docker/compose.yml  │    │ (this repo's         │         │
│  │  in the Cimmeria     │    │  docker/compose.yml) │         │
│  │  repo)               │    │                      │         │
│  │                      │    │                      │         │
│  │  • cimmeria-server   │    │  • cimmeria-mcp ─────┼──┐      │
│  │  • watchtower        │    │  • postgres          │  │      │
│  │  • signoz frontend   │    │                      │  │      │
│  │  • clickhouse        │    └──────────────────────┘  │      │
│  │  • otel-collector ◀──┼─────────── OTLP ─────────────┘      │
│  │  • zookeeper-1       │                                     │
│  │  • alertmanager      │                                     │
│  │  • query-service     │                                     │
│  └──────────────────────┘                                     │
│                                                               │
│   (shared network: cimmeria_default, joined by MCP            │
│    compose as `signoz` external network)                      │
└─────────────────────────┬─────────────────────────────────────┘
                          │ HTTPS (Azure OpenAI calls)
                          ▼
                  ┌───────────────┐
                  │  Azure OpenAI │
                  └───────────────┘
```

The MCP server reaches the OTel collector via the shared docker
network — no `host.docker.internal` hop, no `OTLP_BIND=0.0.0.0`
exposure on the host's network interface. The Cimmeria compose's
SigNoz collector is bound to loopback only (per the security
posture in `docs/operations/signoz-deployment.md` of the Cimmeria
repo); the MCP container talks to it container-to-container.

## Bring-up steps (once the C# refactor lands)

```bash
# 1. Make sure Cimmeria's stack is running on this host.
ssh colo "docker compose -f /opt/cimmeria/compose.yml ps"

# 2. Drop this compose + init scripts onto the colo.
scp -r docker/ colo:/opt/cimmeria-mcp/

# 3. Configure env (one-time):
ssh colo
cd /opt/cimmeria-mcp/docker
cp .env.example .env
# Fill in POSTGRES_PASSWORD, OPENAI_*, MCP_API_KEY, SIGNOZ_NETWORK_NAME

# 4. Bring it up:
docker compose --env-file .env -f compose.yml up -d
```

On first start, the Postgres container runs `postgres-init/01_schema.sql`
against a fresh database, creating the pgvector tables and indexes.
Subsequent restarts skip the init script.

## What this PR completes vs leaves open

| Step | Status |
|---|---|
| Functions → ASP.NET Core MCP-over-HTTP | ✅ done |
| Cosmos DB knowledge-graph → Postgres `kg_vertices` + `kg_edges` | ✅ done (graph service rewritten) |
| Azure AI Search → pgvector + pg_trgm | ✅ done (search service rewritten) |
| Azure SignalR Service → in-process `AspNetCore.SignalR.Hub` | ✅ done |
| App Insights / Monitor → OTLP | ✅ done (auto-instrumented via `OpenTelemetry.Instrumentation.AspNetCore`) |
| Indexer (Cosmos change-feed → scheduled `BackgroundService`) | ⚠️ shell only — heartbeat updates `mcp_indexer_state`, actual indexing body is TODO |
| AI skills (14 prompt-engineered tools, ~1300 lines in `CimmeriaSummarizationService`) | ⚠️ stubbed — each tool is registered and visible in `tools/list`, but returns a structured `port_pending` JSON envelope. Search + graph tools work; AI skills need their own focused port PR. |
| Data migration from Cosmos to Postgres | ❌ operator task — see "Data migration" below |

The 6 search tools and 14 knowledge-graph tools work end-to-end
against Postgres + pgvector after the data migration runs. The AI
skills surface in the catalogue so MCP clients still see all 34
tools, but until the prompt-engineering port lands they direct the
caller at the search + graph tools as the immediate workaround.

## Code structure after the refactor

```text
src/CimmeriaMcp.Functions/        (folder kept for git history; AssemblyName=CimmeriaMcp)
├── CimmeriaMcp.Functions.csproj   (Web SDK; no Azure Functions deps)
├── Program.cs                     (ASP.NET Core host + DI + OTel + endpoints)
├── Mcp/
│   ├── McpToolAttribute.cs        ([McpTool("name", "description")])
│   ├── McpPropertyAttribute.cs    ([McpProperty("name", "description", isRequired:true)])
│   ├── JsonRpc.cs                 (JSON-RPC 2.0 + MCP wire types)
│   ├── McpDispatcher.cs           (reflection-based tool registry)
│   └── McpEndpoint.cs             (POST /mcp handler)
├── Auth/BearerAuthMiddleware.cs   (constant-time bearer-token check)
├── Hubs/CimmeriaHub.cs            (in-process SignalR Hub for tool-event broadcasts)
├── Services/
│   ├── CimmeriaSearchService.cs   (Npgsql + pgvector — 6 search tools)
│   ├── CimmeriaGraphService.cs    (Npgsql — 14 graph tools)
│   ├── CimmeriaSummarizationService.cs  (stub — AI skills awaiting port)
│   ├── SignalRBroadcastService.cs (wraps every tool with broadcast + error envelope)
│   └── IndexerService.cs          (BackgroundService — heartbeat only)
└── Tools/
    ├── CimmeriaSearchTools.cs     (6 MCP tools)
    ├── CimmeriaGraphTools.cs      (14 MCP tools)
    └── CimmeriaAiTools.cs         (14 MCP tools)
```

## Data migration (operator task)

The Postgres schema is empty after first start. To populate it from
the existing Cosmos data:

1. **Export from Cosmos.** For each container, use `azcopy` or the
   Cosmos data-migration tool to dump as JSONL.
2. **Map fields.** The Postgres tables preserve the snake_case JSONB
   shape, so most fields go directly into the `properties` column.
   Per-table mapping:
   - `code-chunks` → `code_chunks` (`id`, `source_project`, `file_path`,
     `language`, `content`, `embedding`, `metadata` jsonb).
   - `knowledge-graph` doc_type=vertex → `kg_vertices` (`id`, `pk`,
     `vertex_type` from `label`, `name`, the rest goes into `properties`).
   - `knowledge-graph` doc_type=edge → `kg_edges` (`id`, `pk`,
     `from_id`, `to_id`, `edge_type` from `label`, the rest into
     `properties`).
   - `leases` → discard (no Postgres equivalent needed).
3. **Import via `COPY`.** A one-shot script per table, e.g.:

   ```sh
   psql $DATABASE_URL -c "\COPY code_chunks (id, source_project, file_path, language, content, embedding, metadata) FROM 'chunks.csv' WITH (FORMAT csv)"
   ```

4. **Re-embed if dimensions change.** The schema uses
   `vector(1536)` to match `text-embedding-3-small` defaults. If the
   Cosmos data was truncated to a different dimensionality (the
   cloud deployment used 505-dim — see CLAUDE.md), drop the embedding
   column, re-create at 1536-dim, and re-embed via the Azure OpenAI
   client.

A scripted migration tool is a follow-up; this PR doesn't ship one.

## Why the layered approach

The compose + schema landed first because:

1. It locked down the env-var contract so the C# refactor had a
   stable target.
2. It let ops validate the network topology (Cloudflare Tunnel
   routing, OTLP reachability, Postgres bootstrap) before code work
   began.
3. It made the rollback story crisp: until the colo MCP answered
   real queries with the same shapes, the cloud deployment stayed
   the production path.

With this PR, the colo deployment is functional for search + graph
tools. The AI skills port lands as a follow-up PR with its own focus
on prompt-engineering fidelity.

## Trade-offs worth knowing

- **No change-feed equivalent in Postgres.** The Cosmos `leases`-driven
  indexer fired on every code-chunk write. In Postgres we get a
  scheduled poll instead. Indexer freshness becomes "≤5 minutes
  stale" rather than "near real-time". The current cloud deployment
  also runs a 5-min debounce, so this is a wash.
- **Postgres backups become an operator concern.** Cosmos handled
  continuous backups for free. For the colo, set up `pg_dump` on a
  cron or use Postgres's WAL archiving — TBD when retention SLAs
  matter.
- **Cold start is now "wait for Postgres init + Docker pull"** instead
  of "wait for the Functions runtime to thaw". Faster after the first
  start; identical for subsequent restarts.
- **Single-host availability.** The colo is one machine; if it dies,
  the MCP server dies with it. Azure's regional redundancy is gone.
  Acceptable for an internal dev tool, less acceptable if this ever
  becomes a customer-facing API.
