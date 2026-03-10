using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace CimmeriaMcp.Functions.Functions;

public class SignalRHub
{
    [Function("negotiate")]
    public HttpResponseData Negotiate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", "options", Route = "negotiate")]
        HttpRequestData req,
        [SignalRConnectionInfoInput(HubName = "cimmeria")]
        string connectionInfo)
    {
        // Handle CORS preflight
        if (req.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            var preflight = req.CreateResponse(HttpStatusCode.NoContent);
            preflight.Headers.Add("Access-Control-Allow-Origin", "*");
            preflight.Headers.Add("Access-Control-Allow-Methods", "POST, OPTIONS");
            preflight.Headers.Add("Access-Control-Allow-Headers", "Content-Type, x-ms-signalr-user-id");
            preflight.Headers.Add("Access-Control-Max-Age", "86400");
            return preflight;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.WriteString(connectionInfo);
        return response;
    }

    public static SignalRMessageAction CreateBroadcast(string toolName, long durationMs, string status)
    {
        return new SignalRMessageAction("toolInvocation")
        {
            Arguments =
            [
                new
                {
                    tool = toolName,
                    durationMs,
                    status,
                    timestamp = DateTimeOffset.UtcNow.ToString("o")
                }
            ]
        };
    }
}
