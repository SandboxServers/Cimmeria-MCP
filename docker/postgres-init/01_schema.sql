-- Cimmeria-MCP Postgres schema.
--
-- Runs once on first Postgres start (the official postgres image
-- executes every .sql file in /docker-entrypoint-initdb.d/ in
-- lexical order against the freshly initialised database).
--
-- Replaces three Azure data stores with one Postgres database:
--
--   Azure AI Search `cimmeria-code` index  ──┐
--   Cosmos DB `code-chunks` container       ──┼──▶ table: code_chunks
--                                              │     (pgvector HNSW for similarity,
--                                              │      pg_trgm for fuzzy text)
--                                              │
--   Cosmos DB `knowledge-graph` container   ──┼──▶ tables: kg_vertices, kg_edges
--                                              │     (4,801 vertices + 4,340 edges
--                                              │      after data migration)
--                                              │
--   Cosmos DB `leases` container            ──┴──▶ (dropped — no change feed
--                                                    equivalent. The indexer
--                                                    becomes a scheduled job.)
--
-- Field naming preserves the snake_case the Cosmos schema used so
-- the data migration can be a JSONL export / `\copy` round-trip
-- without column renames.

-- ──────────────────────────────────────────────────────────────────
-- Extensions
-- ──────────────────────────────────────────────────────────────────

-- pgvector — supplies the `vector` type, cosine/L2 ops, and HNSW
-- indexing. Mirrors the HNSW index Azure AI Search ran on top of
-- the same 1536-dim text-embedding-3-small embeddings.
CREATE EXTENSION IF NOT EXISTS vector;

-- pg_trgm — fast fuzzy text matching for the keyword fallback path
-- (when the vector search returns nothing relevant). Replaces the
-- BM25 lexical leg of Azure AI Search's hybrid query.
CREATE EXTENSION IF NOT EXISTS pg_trgm;

-- ──────────────────────────────────────────────────────────────────
-- code_chunks
-- ──────────────────────────────────────────────────────────────────
-- Embedded source-code snippets across cimmeria-server, sgw-client,
-- and bigworld-engine. One row per chunk; large files split into
-- multiple chunks during indexing (the `chunk_index` / `total_chunks`
-- pair lets a query reconstruct the surrounding context).
--
-- The HNSW index covers cosine similarity at 1536-dim. Cosine is the
-- right operator for text-embedding-3-small. If you bump the
-- embedding model dimensions, change `vector(1536)` AND drop+rebuild
-- the HNSW index — pgvector cannot resize in place.

CREATE TABLE code_chunks (
    id              TEXT PRIMARY KEY,
    source_project  TEXT NOT NULL,            -- 'cimmeria-server' | 'sgw-client' | 'bigworld-engine'
    file_path       TEXT NOT NULL,
    language        TEXT,                     -- 'rust' | 'cpp' | 'lua' | 'layout' | …
    content         TEXT NOT NULL,
    embedding       vector(1536) NOT NULL,
    chunk_index     INT NOT NULL DEFAULT 0,
    total_chunks    INT NOT NULL DEFAULT 1,
    -- JSONB for everything that didn't fit a typed column. Carries
    -- the same metadata bag the Cosmos `code-chunks` documents had,
    -- so the search service can keep returning whichever filterable
    -- fields the existing tools expect.
    metadata        JSONB NOT NULL DEFAULT '{}',
    indexed_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Vector similarity — primary search index.
-- HNSW is faster at query time than IVFFlat for read-heavy
-- workloads, at the cost of slower index builds. Build parameters
-- match SigNoz's recommended defaults for ~10k-1M rows. Tune
-- `ef_construction` higher if recall on the long tail matters more
-- than build speed.
CREATE INDEX code_chunks_embedding_hnsw
    ON code_chunks
    USING hnsw (embedding vector_cosine_ops)
    WITH (m = 16, ef_construction = 64);

-- Per-source lookup (replaces Cosmos partition-key filtering on /source_project).
CREATE INDEX code_chunks_source_path_idx
    ON code_chunks (source_project, file_path);

-- Fuzzy text — the hybrid fallback when vector search misses.
-- gin_trgm_ops gives `%`/`<%>` similarity and `ILIKE` acceleration.
CREATE INDEX code_chunks_content_trgm_idx
    ON code_chunks
    USING gin (content gin_trgm_ops);

-- JSONB lookup for metadata-driven filters (extension, language,
-- file-type, owner, etc.).
CREATE INDEX code_chunks_metadata_gin
    ON code_chunks
    USING gin (metadata);

-- ──────────────────────────────────────────────────────────────────
-- kg_vertices
-- ──────────────────────────────────────────────────────────────────
-- Replaces the `doc_type=vertex` documents in the Cosmos
-- `knowledge-graph` container. ~4,801 rows at last count, covering
-- entities, methods, properties, enums, types, game defs, C++
-- classes, and worlds.
--
-- Cosmos used a freeform `pk` for partitioning; we retain it for
-- 1:1 round-tripping but it is NOT used as a query key here — the
-- typed columns (`vertex_type`, `name`) are.

CREATE TABLE kg_vertices (
    id              TEXT PRIMARY KEY,
    pk              TEXT NOT NULL,
    vertex_type     TEXT NOT NULL,            -- 'entity' | 'method' | 'property' | 'enum' | 'type' | 'game_def' | 'cpp_class' | 'world'
    name            TEXT NOT NULL,
    -- JSONB carries the per-type freeform property bag: method
    -- signatures, property types, enum values, game-def fields, etc.
    -- Snake_case keys preserved from Cosmos (`method_type`,
    -- `data_type`, etc.) so the service-layer mappers don't change.
    properties      JSONB NOT NULL DEFAULT '{}'
);

CREATE INDEX kg_vertices_type_idx ON kg_vertices (vertex_type);
CREATE INDEX kg_vertices_name_idx ON kg_vertices (name);
CREATE INDEX kg_vertices_type_name_idx ON kg_vertices (vertex_type, name);
CREATE INDEX kg_vertices_properties_gin ON kg_vertices USING gin (properties);

-- ──────────────────────────────────────────────────────────────────
-- kg_edges
-- ──────────────────────────────────────────────────────────────────
-- Replaces `doc_type=edge` documents. ~4,340 rows. Each row is a
-- typed relationship between two vertices.
--
-- ON DELETE CASCADE preserves the Cosmos invariant: deleting a
-- vertex removed its dangling edges automatically. Without this,
-- a partial reindex would leave orphan edges that the graph queries
-- would treat as live.

CREATE TABLE kg_edges (
    id              TEXT PRIMARY KEY,
    pk              TEXT NOT NULL,
    from_id         TEXT NOT NULL REFERENCES kg_vertices (id) ON DELETE CASCADE,
    to_id           TEXT NOT NULL REFERENCES kg_vertices (id) ON DELETE CASCADE,
    edge_type       TEXT NOT NULL,            -- 'extends' | 'implements' | 'calls' | 'reads' | 'writes' | 'replicates' | …
    properties      JSONB NOT NULL DEFAULT '{}'
);

-- Forward traversal: "what does this vertex point at, by edge type?"
CREATE INDEX kg_edges_from_type_idx ON kg_edges (from_id, edge_type);
-- Reverse traversal: "who points at this vertex?"
CREATE INDEX kg_edges_to_type_idx ON kg_edges (to_id, edge_type);
-- Whole-graph queries by edge type (e.g. "all inheritance edges").
CREATE INDEX kg_edges_type_idx ON kg_edges (edge_type);
CREATE INDEX kg_edges_properties_gin ON kg_edges USING gin (properties);

-- ──────────────────────────────────────────────────────────────────
-- mcp_indexer_state
-- ──────────────────────────────────────────────────────────────────
-- Tracks the last-indexed timestamp per source project. The
-- background indexer in the ASP.NET MCP host polls this table to
-- decide whether to rebuild any per-source slice of `code_chunks`.
--
-- Replaces the Cosmos `leases` container that drove the change-feed
-- trigger. There is no change feed in plain Postgres, so the
-- indexer becomes a scheduled job; this table is just its memory.

CREATE TABLE mcp_indexer_state (
    source_project   TEXT PRIMARY KEY,
    last_indexed_at  TIMESTAMPTZ NOT NULL,
    last_commit_sha  TEXT,
    last_run_status  TEXT NOT NULL DEFAULT 'unknown',  -- 'success' | 'partial' | 'failed' | 'unknown'
    last_run_notes   TEXT
);

-- ──────────────────────────────────────────────────────────────────
-- Read-only role for the MCP service
-- ──────────────────────────────────────────────────────────────────
-- The `mcp` user (created by the postgres image entrypoint from the
-- POSTGRES_USER env var) is the DB owner and has full DDL access.
-- That's appropriate for the indexer process. For request-path
-- queries from MCP tools we'd ideally route through a read-only
-- role, but the current C# layer uses a single connection. Leaving
-- this commented as a future hardening target.
--
-- CREATE ROLE mcp_reader LOGIN PASSWORD '<set in env>';
-- GRANT CONNECT ON DATABASE cimmeria_mcp TO mcp_reader;
-- GRANT USAGE ON SCHEMA public TO mcp_reader;
-- GRANT SELECT ON ALL TABLES IN SCHEMA public TO mcp_reader;
