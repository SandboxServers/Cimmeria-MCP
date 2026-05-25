using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CimmeriaMcp.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace CimmeriaMcp.Functions.Tests;

public class BearerAuthMiddlewareTests
{
    private static IConfiguration BuildConfig(string? key)
    {
        var settings = new Dictionary<string, string?>();
        if (key is not null) settings["MCP_API_KEY"] = key;
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private static BearerAuthMiddleware NewMiddleware(IConfiguration config, RequestDelegate next)
        => new(next, config);

    private static DefaultHttpContext NewContext(string path, string? authHeader = null)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Response.Body = new MemoryStream();
        if (authHeader is not null) ctx.Request.Headers.Authorization = authHeader;
        return ctx;
    }

    [Fact]
    public async Task Health_Path_Is_Anonymous()
    {
        var nextCalled = false;
        var mw = NewMiddleware(BuildConfig("secret"), _ => { nextCalled = true; return Task.CompletedTask; });

        await mw.InvokeAsync(NewContext("/health"));

        // No auth header, no rejection — health endpoints must serve
        // liveness checks without a key.
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Hub_Path_Is_Anonymous()
    {
        var nextCalled = false;
        var mw = NewMiddleware(BuildConfig("secret"), _ => { nextCalled = true; return Task.CompletedTask; });

        await mw.InvokeAsync(NewContext("/hubs/cimmeria"));

        // SignalR hub handshake skips the bearer middleware too;
        // the negotiate exchange is unauth in this deployment (LAN /
        // tunnel front it).
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Missing_Header_Returns_401_With_WwwAuthenticate()
    {
        var mw = NewMiddleware(BuildConfig("secret"), _ => Task.CompletedTask);

        var ctx = NewContext("/mcp");
        await mw.InvokeAsync(ctx);

        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
        Assert.Equal("Bearer", ctx.Response.Headers.WWWAuthenticate.ToString());
    }

    [Fact]
    public async Task Wrong_Token_Returns_401()
    {
        var mw = NewMiddleware(BuildConfig("secret"), _ => Task.CompletedTask);

        var ctx = NewContext("/mcp", "Bearer wrong-token");
        await mw.InvokeAsync(ctx);

        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Correct_Token_Passes_Through()
    {
        var nextCalled = false;
        var mw = NewMiddleware(BuildConfig("secret"), _ => { nextCalled = true; return Task.CompletedTask; });

        var ctx = NewContext("/mcp", "Bearer secret");
        await mw.InvokeAsync(ctx);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Missing_Server_Config_Returns_503()
    {
        // Operator forgot to set MCP_API_KEY. Fail closed — refusing
        // every request is safer than serving them unauthenticated.
        var mw = NewMiddleware(BuildConfig(key: null), _ => Task.CompletedTask);

        var ctx = NewContext("/mcp", "Bearer anything");
        await mw.InvokeAsync(ctx);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, ctx.Response.StatusCode);
    }
}
