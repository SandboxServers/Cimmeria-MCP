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

## C# refactor — what still has to happen

This compose file is the **target topology**. The current
`src/CimmeriaMcp.Functions/` project will not run against the
services defined here until the following changes land:

### Step 1 — Functions → ASP.NET Core MCP-over-HTTP

- Delete dependency on `Microsoft.Azure.Functions.Worker.Extensions.Mcp`.
- Switch from `[Function]` + `[McpToolTrigger]` to whichever transport
  the official MCP C# SDK exposes (or a hand-rolled JSON-RPC over HTTP
  endpoint at e.g. `POST /mcp`).
- Replace the Functions auth model (`x-functions-key`) with
  `Authorization: Bearer ${MCP_API_KEY}` middleware.
- Add an `/health` endpoint for the Dockerfile healthcheck.

### Step 2 — Cosmos DB → Postgres

- Replace the `Microsoft.Azure.Cosmos` dependency with `Npgsql` (+
  `Microsoft.EntityFrameworkCore` if EF is preferred over raw
  Npgsql; raw Npgsql keeps things smaller).
- `CimmeriaGraphService` rewrites its 14 query methods against the
  `kg_vertices` + `kg_edges` tables. Snake_case property fields stay
  in JSONB so the existing JSON shape passed to LLMs is unchanged.
- Delete `Functions/IndexerTrigger.cs`. Replace with a
  `BackgroundService` that polls `mcp_indexer_state` on a 5-minute
  cadence and re-runs the indexer when source repos move.

### Step 3 — Azure AI Search → pgvector

- Delete the `Azure.Search.Documents` dependency.
- `CimmeriaSearchService.SearchAsync` issues two queries:
  - Primary: cosine similarity ORDER BY `embedding <=> :query_embedding`
    LIMIT k.
  - Fallback when cosine returns nothing useful: trigram similarity
    via `pg_trgm` (`content % :keyword`).
  - The "hybrid" score is the weighted sum of the two — same shape
    the Azure AI Search hybrid query returned.

### Step 4 — Azure SignalR Service → in-process Hub

- Delete the `Microsoft.Azure.Functions.Worker.Extensions.SignalRService`
  dependency.
- `SignalRBroadcastService` becomes a plain
  `Microsoft.AspNetCore.SignalR.Hub`. The JWT-token-mint REST POST
  path goes away; clients connect to the hub via WebSocket/SSE.
- Tool invocation telemetry continues to broadcast — just over the
  in-process hub instead of through Azure.

### Step 5 — App Insights → OTLP

- Delete `Functions/MetricsEndpoint.cs` and `Services/MetricsService.cs`.
- Add `OpenTelemetry.Exporter.OpenTelemetryProtocol` to the project.
- Wire the SDK to read `OTEL_EXPORTER_OTLP_ENDPOINT` /
  `OTEL_SERVICE_NAME` env vars — the same contract the Cimmeria
  Rust side already uses. Application Insights data goes away;
  SigNoz dashboards become the operational pane of glass.

### Step 6 — Data migration

- Export Cosmos containers to JSONL via `azcopy` / the Cosmos SDK.
- For each container, run a one-shot `COPY ... FROM STDIN` import
  into the matching Postgres table. The JSONB columns accept the
  Cosmos document shape verbatim, so the import is one
  `jq '{id, pk, vertex_type: .vertex_type, name, properties: .}'`
  pipeline per table.
- Re-embed `code_chunks` if you want them fresh, or copy the existing
  Azure AI Search embeddings over (same model + dimensions).

## Why the layered approach

The compose + schema lands first because:

1. It locks down the env-var contract so the C# refactor has a
   stable target (no rewriting handler signatures three times
   chasing config changes).
2. It lets ops bring up Postgres + an empty MCP container on the
   colo for end-to-end network validation (Cloudflare Tunnel
   routing, OTLP reachability) before the code work begins.
3. It makes the rollback story clear: until the new compose is
   producing useful query responses, the Azure-Functions deployment
   stays the production path. Cut over only after the colo MCP
   answers the same queries with the same shapes.

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
