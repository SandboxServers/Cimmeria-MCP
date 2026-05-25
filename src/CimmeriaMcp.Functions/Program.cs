using CimmeriaMcp.Auth;
using CimmeriaMcp.Hubs;
using CimmeriaMcp.Services;
using Npgsql;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Pgvector.Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Environment-variable config takes precedence over appsettings.
builder.Configuration.AddEnvironmentVariables();

// ── DI registration ──────────────────────────────────────────────

// Postgres + pgvector. The data source is constructed once and shared
// across the app — Npgsql pools connections internally.
// `UseVector()` registers the pgvector type mapping so reading and
// writing `vector` columns "just works".
var connectionString = builder.Configuration["DATABASE_URL"]
    ?? throw new InvalidOperationException("DATABASE_URL is not configured.");

var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
dataSourceBuilder.UseVector();
builder.Services.AddSingleton(dataSourceBuilder.Build());

// Application services. The two data services + the summarization
// stub are DI singletons; the official MCP SDK resolves them when
// it instantiates the tool classes per request.
builder.Services.AddSingleton<CimmeriaSearchService>();
builder.Services.AddSingleton<CimmeriaGraphService>();
builder.Services.AddSingleton<CimmeriaSummarizationService>();
builder.Services.AddSingleton<SignalRBroadcastService>();

// Background indexer — periodic poll replaces the Cosmos change-feed
// Functions trigger. Heartbeats `mcp_indexer_state`; full body is a
// follow-up.
builder.Services.AddHostedService<IndexerService>();

// ── MCP server (official Anthropic + Microsoft SDK) ──────────────
//
// `AddMcpServer()` registers the MCP infrastructure;
// `WithHttpTransport()` wires the Streamable HTTP transport that
// `MapMcp()` exposes; `WithToolsFromAssembly()` reflects the calling
// assembly for every class marked `[McpServerToolType]` and registers
// each `[McpServerTool]` method (parameter descriptions come from the
// `[Description]` attribute on each parameter). One-stop dispatch —
// no hand-rolled JSON-RPC parsing on our side.
builder.Services.AddMcpServer()
    .WithHttpTransport(options =>
    {
        // Stateless = each request is independent (no per-client
        // session state held on the server). Right for our use case:
        // every tool call is self-contained, no resumable sessions.
        options.Stateless = true;
    })
    .WithToolsFromAssembly();

// SignalR — in-process Hub for live dashboards. Replaces the Azure
// SignalR Service. Tool-invocation timing now comes from OTLP traces
// instead of an explicit broadcast wrapper (the SDK handles dispatch
// internally, so we don't have a hook to wrap every call).
builder.Services.AddSignalR();

// ── Observability — OTLP to the SigNoz collector on the same host ──
//
// The OTel SDK reads OTEL_EXPORTER_OTLP_ENDPOINT, OTEL_SERVICE_NAME,
// and OTEL_RESOURCE_ATTRIBUTES from env. AspNetCore instrumentation
// captures one trace per inbound request — including each MCP
// JSON-RPC call routed through `MapMcp()` — so SigNoz sees tool
// invocations as service operations without us adding anything.
var serviceName = builder.Configuration["OTEL_SERVICE_NAME"] ?? "cimmeria-mcp";
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService(serviceName))
    .WithTracing(t => t
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter());

builder.Services.AddHealthChecks();

var app = builder.Build();

// ── Middleware pipeline ──────────────────────────────────────────

// Bearer-token auth applies to the MCP route. Health + SignalR hub
// are exempted inside the middleware itself so liveness checks and
// the broadcast hub don't need the production key.
app.UseMiddleware<BearerAuthMiddleware>();

// ── Endpoint routing ─────────────────────────────────────────────

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = serviceName,
    timestamp = DateTimeOffset.UtcNow.ToString("o"),
}));

app.MapGet("/ready", async (NpgsqlDataSource db, CancellationToken ct) =>
{
    try
    {
        await using var cmd = db.CreateCommand("SELECT 1");
        var v = await cmd.ExecuteScalarAsync(ct);
        return v is int i && i == 1
            ? Results.Ok(new { status = "ready", postgres = "ok" })
            : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception ex)
    {
        return Results.Json(new { status = "not_ready", error = ex.Message },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

// Official MCP SDK transport — serves Streamable HTTP at the root of
// `/mcp` plus SSE fallback at `/mcp/sse` + `/mcp/message`. Handles
// the entire JSON-RPC 2.0 protocol (initialize / tools/list /
// tools/call / ping / notifications/*) without us touching it.
app.MapMcp("/mcp");

// SignalR hub for live tool-invocation events. The broadcast service
// stays available via DI for tools that want to push dashboard
// updates explicitly — but the SDK's dispatcher doesn't expose a
// "wrap every tool" hook, so most observability lives in OTLP now.
app.MapHub<CimmeriaHub>("/hubs/cimmeria");

app.Run();
