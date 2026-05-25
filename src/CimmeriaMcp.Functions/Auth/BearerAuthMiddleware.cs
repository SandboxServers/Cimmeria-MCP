using Microsoft.AspNetCore.Http;

namespace CimmeriaMcp.Auth;

/// <summary>
/// Static-bearer-token middleware. Replaces the Azure Functions
/// `x-functions-key` system-key auth the cloud deployment used.
///
/// Expects every request to the protected route to carry
/// `Authorization: Bearer &lt;MCP_API_KEY&gt;`. The constant-time
/// comparison is implemented via fixed-time string equality to keep
/// timing oracles off the table — the MCP server is reachable from
/// the public internet via the Cloudflare Tunnel, so the threat model
/// includes a local attacker testing token guesses against response
/// timing.
///
/// Health and metrics endpoints are exempt — they're explicitly
/// anonymous so liveness checks and observability scrapers don't
/// need the production secret.
/// </summary>
public sealed class BearerAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string? _expectedToken;
    private readonly HashSet<string> _anonymousPaths;

    public BearerAuthMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _expectedToken = config["MCP_API_KEY"];
        _anonymousPaths = new(StringComparer.OrdinalIgnoreCase)
        {
            "/health",
            "/ready",
        };
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        // Skip auth for the explicit anonymous list and for non-MCP
        // routes (the SignalR negotiate endpoint uses its own auth
        // shape and is wired separately).
        var path = ctx.Request.Path.Value ?? "";
        if (_anonymousPaths.Contains(path)
            || path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(ctx);
            return;
        }

        // No configured token → fail closed. Operating without a key
        // in any non-dev environment is an operator error, not a
        // missing-feature.
        if (string.IsNullOrEmpty(_expectedToken))
        {
            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await ctx.Response.WriteAsync("MCP_API_KEY not configured on the server.");
            return;
        }

        var header = ctx.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.Ordinal))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            ctx.Response.Headers.WWWAuthenticate = "Bearer";
            return;
        }

        var presented = header.Substring("Bearer ".Length).Trim();
        if (!FixedTimeEquals(presented, _expectedToken))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            ctx.Response.Headers.WWWAuthenticate = "Bearer";
            return;
        }

        await _next(ctx);
    }

    /// <summary>
    /// Constant-time string equality. Compares every byte regardless
    /// of length mismatch so an attacker can't probe token length via
    /// timing differences.
    /// </summary>
    private static bool FixedTimeEquals(string a, string b)
    {
        var bytesA = System.Text.Encoding.UTF8.GetBytes(a);
        var bytesB = System.Text.Encoding.UTF8.GetBytes(b);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(bytesA, bytesB);
    }
}
