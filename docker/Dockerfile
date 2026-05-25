# Cimmeria-MCP container — multi-stage .NET 10 build.
#
# Produces an ASP.NET Core MCP server image suitable for the colo
# Docker compose. The image hosts the official `ModelContextProtocol.AspNetCore`
# SDK on Kestrel, listening on port 8080. Companion services
# (Postgres + pgvector) are defined alongside in
# [docker/compose.yml](./compose.yml).
#
# # Image layout
#
#   /app/CimmeriaMcp.dll        — the published binary
#   /app/...                    — supporting assemblies + runtime config
#
# The project folder on disk is `src/CimmeriaMcp.Functions/` (the
# `Functions` suffix is preserved for git history; the Azure
# Functions deps inside it are gone). The csproj sets
# `AssemblyName=CimmeriaMcp` so the published binary matches the
# entrypoint below.

# ── Build stage ────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, with only the csproj copied, so package restore is
# cached across most edits. The test project is excluded by
# .dockerignore — it's not part of the runtime image and pulling it
# in would just bloat the build context.
COPY src/CimmeriaMcp.Functions/CimmeriaMcp.Functions.csproj ./CimmeriaMcp.Functions/
RUN dotnet restore CimmeriaMcp.Functions/CimmeriaMcp.Functions.csproj

# Now copy the rest of the source and publish a self-contained
# binary in Release mode. PublishTrimmed=false because trimming +
# reflection (ASP.NET routing / DI / the MCP SDK's tool discovery)
# is a known hazard area.
COPY src/CimmeriaMcp.Functions ./CimmeriaMcp.Functions
RUN dotnet publish CimmeriaMcp.Functions/CimmeriaMcp.Functions.csproj \
    -c Release -o /app \
    --no-restore \
    -p:PublishTrimmed=false

# ── Runtime stage ──────────────────────────────────────────────────
# ASP.NET base image (not the Azure Functions image — we're not a
# Functions app anymore). The MCP-over-HTTP server runs as plain
# Kestrel inside this container.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# curl for the compose healthcheck (`GET /health`). The aspnet base
# image is debian-slim; the install is a few hundred kilobytes.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Non-root user for the runtime — defence in depth. Kestrel binds to
# port 8080 (>1024) so no CAP_NET_BIND_SERVICE is needed.
RUN groupadd -r mcp && useradd -r -g mcp -d /app -s /sbin/nologin mcp
COPY --from=build --chown=mcp:mcp /app .
USER mcp

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

# Image-level healthcheck. compose.yml has its own healthcheck too;
# the docker daemon's check helps `docker ps` and any orchestrator
# that reads HEALTHCHECK without parsing the compose file.
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "CimmeriaMcp.dll"]
