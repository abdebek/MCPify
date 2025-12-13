using MCPify.Core;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace MCPify.Hosting;

public class McpContextMiddleware
{
    private readonly RequestDelegate _next;

    public McpContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext httpContext, IMcpContextAccessor accessor)
    {
        // Attempt to retrieve sessionId and connectionId from HttpContext.Items
        // These keys are placeholders. Real keys would be provided by ModelContextProtocol.AspNetCore
        if (httpContext.Items.TryGetValue("McpSessionId", out var sessionIdObj) && sessionIdObj is string sessionId)
        {
            accessor.SessionId = sessionId;
        }

        if (httpContext.Items.TryGetValue("McpConnectionId", out var connectionIdObj) && connectionIdObj is string connectionId)
        {
            accessor.ConnectionId = connectionId;
        }

        // Set the current context for AsyncLocal to ensure it's available downstream
        McpContextAccessor.CurrentContext = new McpContextAccessor.McpContext
        {
            SessionId = accessor.SessionId,
            ConnectionId = accessor.ConnectionId,
            AccessToken = accessor.AccessToken
        };

        try
        {
            await _next(httpContext);
        }
        finally
        {
            // Clear the context to prevent leakage to other requests
            McpContextAccessor.CurrentContext = null;
        }
    }
}