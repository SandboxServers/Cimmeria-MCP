# Cimmeria-MCP container — multi-stage .NET 10 build.
#
# Status: SPECULATIVE. The current `src/CimmeriaMcp.Functions/`
# project targets the Azure Functions runtime and will not build
# against this Dockerfile until the migration in
# `docs/colo-deployment.md` lands. Tracked there as Step 1
# (Functions → plain ASP.NET Core MCP host).
#
# The Dockerfile is committed first so the deployment surface and
# CI pipeline can be locked in before the C# refactor; pushing a
# working image is gated on that refactor.

# ── Build stage ────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, with only csproj files copied, so package restore
# is cached across most edits. The refactored project name is
# expected to be `CimmeriaMcp` (no `.Functions` suffix once the
# Azure-Functions host is gone); both paths handled below so the
# Dockerfile keeps working through the rename.
COPY src/CimmeriaMcp/*.csproj ./CimmeriaMcp/ 2>/dev/null || true
COPY src/CimmeriaMcp.Functions/*.csproj ./CimmeriaMcp.Functions/ 2>/dev/null || true
RUN if [ -d CimmeriaMcp ]; then \
        dotnet restore CimmeriaMcp/CimmeriaMcp.csproj; \
    else \
        dotnet restore CimmeriaMcp.Functions/CimmeriaMcp.Functions.csproj; \
    fi

# Now copy the rest of the source and build a self-contained binary
# in Release mode. `PublishTrimmed=false` because trimming + reflection
# in ASP.NET routing / DI is a hazard area; revisit only if image
# size becomes a real problem.
COPY src/ ./
RUN if [ -d CimmeriaMcp ]; then \
        dotnet publish CimmeriaMcp/CimmeriaMcp.csproj \
            -c Release -o /app \
            --no-restore \
            -p:PublishTrimmed=false; \
    else \
        dotnet publish CimmeriaMcp.Functions/CimmeriaMcp.Functions.csproj \
            -c Release -o /app \
            --no-restore \
            -p:PublishTrimmed=false; \
    fi

# ── Runtime stage ──────────────────────────────────────────────────
# ASP.NET base image, NOT the Azure Functions image — the whole point
# of this migration is to drop the Functions runtime. The MCP-over-HTTP
# server runs as a plain Kestrel app.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

# Curl for the healthcheck (`/health` endpoint exposed by the app).
# The aspnet base image is debian-slim; curl is a one-line install.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Non-root user for the runtime — defence in depth. ASP.NET binds to
# port 8080 (>1024) so no CAP_NET_BIND_SERVICE is needed.
RUN groupadd -r mcp && useradd -r -g mcp -d /app -s /sbin/nologin mcp
COPY --from=build --chown=mcp:mcp /app .
USER mcp

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

# The entrypoint binary name will match the project rename. Defaults
# to the current `.Functions.dll` if the rename hasn't happened yet
# (in which case the binary won't actually serve MCP correctly, but
# the container starts and surfaces a clear error in its logs rather
# than failing silently at `docker run`).
ENTRYPOINT ["sh", "-c", "if [ -f CimmeriaMcp.dll ]; then exec dotnet CimmeriaMcp.dll; else exec dotnet CimmeriaMcp.Functions.dll; fi"]
