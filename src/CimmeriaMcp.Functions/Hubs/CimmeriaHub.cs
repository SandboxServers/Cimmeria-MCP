using Microsoft.AspNetCore.SignalR;

namespace CimmeriaMcp.Hubs;

/// <summary>
/// In-process SignalR hub for real-time tool-invocation events.
///
/// Replaces the Azure SignalR Service the cloud deployment used.
/// Clients connect over WebSocket/SSE to `/hubs/cimmeria` and listen
/// for the `toolInvocation` event. The broadcast service (sibling
/// of <see cref="Services.SignalRBroadcastService"/>) pushes events
/// onto this hub via DI-injected <see cref="IHubContext{THub}"/>.
///
/// No methods on the hub itself — this is a one-way push channel.
/// Clients don't invoke server methods through SignalR; the MCP
/// JSON-RPC endpoint at `/mcp` handles all RPC.
/// </summary>
public sealed class CimmeriaHub : Hub
{
    // Empty — broadcast is fan-out from the server side, no
    // client→server calls.
}
