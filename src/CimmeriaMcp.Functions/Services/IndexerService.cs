using Npgsql;

namespace CimmeriaMcp.Services;

/// <summary>
/// Scheduled background indexer. Replaces the Azure Functions
/// Cosmos-DB-change-feed-triggered `IndexerTrigger`. Postgres has no
/// change-feed equivalent, so the indexer becomes a periodic poll
/// over `mcp_indexer_state`.
///
/// This service is a placeholder shell — the actual indexing logic
/// (clone the upstream source repos, chunk + embed code, upsert into
/// `code_chunks`, walk .def + Python + C++ for KG updates) is a
/// follow-up. The shell exists so the deployment topology and the
/// `mcp_indexer_state` table are wired end-to-end now, and the
/// indexer body can drop in without touching plumbing.
/// </summary>
public sealed class IndexerService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    private readonly NpgsqlDataSource _db;
    private readonly ILogger<IndexerService> _log;

    public IndexerService(NpgsqlDataSource db, ILogger<IndexerService> log)
    {
        _db = db;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _log.LogInformation("Indexer started — polling every {Interval}", PollInterval);

        // Ensure the state row exists for every source we track. Upsert
        // pattern: insert if missing, leave the existing row alone if
        // present. Lets the indexer come up cleanly against a fresh
        // database without manual seed data.
        var sources = new[] { "cimmeria-server", "sgw-client", "bigworld-engine" };
        foreach (var s in sources)
        {
            await using var cmd = _db.CreateCommand("""
                INSERT INTO mcp_indexer_state (source_project, last_indexed_at, last_run_status)
                VALUES (@s, '1970-01-01T00:00:00Z', 'unknown')
                ON CONFLICT (source_project) DO NOTHING
                """);
            cmd.Parameters.AddWithValue("s", s);
            try
            {
                await cmd.ExecuteNonQueryAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _log.LogWarning(ex, "Failed to seed indexer state for '{Source}'", s);
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // TODO(indexer-port): walk the source repos and refresh
                // code_chunks + kg_vertices + kg_edges. Until then,
                // just touch the state row so operators can see the
                // indexer is alive.
                await using var cmd = _db.CreateCommand("""
                    UPDATE mcp_indexer_state
                    SET last_indexed_at = now(),
                        last_run_status = 'partial',
                        last_run_notes = 'indexer body not yet ported — see IndexerService.cs'
                    WHERE source_project = ANY(@sources)
                    """);
                cmd.Parameters.AddWithValue("sources", sources);
                await cmd.ExecuteNonQueryAsync(stoppingToken);

                _log.LogDebug("Indexer heartbeat (port pending)");
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _log.LogWarning(ex, "Indexer poll failed");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        _log.LogInformation("Indexer stopping");
    }
}
