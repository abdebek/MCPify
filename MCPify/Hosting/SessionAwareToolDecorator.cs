using System.Linq;
using System.Text.Json;
using MCPify.Core;
using MCPify.Core.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPify.Hosting;

/// <summary>
/// Decorates an McpServerTool to ensure a Session Context exists before execution.
/// This is crucial for Stdio transport where ASP.NET Core middleware does not run.
/// </summary>
public class SessionAwareToolDecorator : McpServerTool
{
    private readonly McpServerTool _innerTool;
    private readonly IServiceProvider _serviceProvider;

    public SessionAwareToolDecorator(McpServerTool innerTool, IServiceProvider serviceProvider)
    {
        _innerTool = innerTool;
        _serviceProvider = serviceProvider;
    }

    public override Tool ProtocolTool => _innerTool.ProtocolTool;

    // Delegate Metadata to the inner tool
    public override IReadOnlyList<object> Metadata => _innerTool.Metadata;

    public override async ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> context, CancellationToken token)
    {
        // Bypass for 'connect' tool - it establishes the session
        if (_innerTool.ProtocolTool.Name.Equals("connect", StringComparison.OrdinalIgnoreCase))
        {
            return await _innerTool.InvokeAsync(context, token);
        }

        // Ensure the RequestContext has Services (it should, but safety first)
        var services = context.Services ?? _serviceProvider;
        var accessor = services.GetService<IMcpContextAccessor>();

        if (accessor != null)
        {
            // Start with the session produced by the MCP server implementation.
            var sessionId = context.Server?.SessionId;

            // For backwards compatibility with older clients that send the sessionId explicitly.
            if (string.IsNullOrEmpty(sessionId) && context.Params?.Arguments != null)
            {
                // Case-insensitive lookup. FirstOrDefault returns default(KeyValuePair) if not found.
                var argEntry = context.Params.Arguments.FirstOrDefault(x => x.Key.Equals("sessionId", StringComparison.OrdinalIgnoreCase));
                
                // Check if we actually found a key (Key will be non-null)
                if (argEntry.Key != null)
                {
                    if (argEntry.Value.ValueKind == JsonValueKind.String)
                    {
                        sessionId = argEntry.Value.GetString();
                    }
                    else
                    {
                        // Fallback for other types (e.g. number/boolean) if user sent unexpected data
                        sessionId = argEntry.Value.ToString();
                    }
                }
            }

            // Resolve the actual Principal if we have a Handle (for Lazy Auth support)
            var sessionMap = services.GetService<ISessionMap>();
            if (sessionMap != null && !string.IsNullOrEmpty(sessionId))
            {
                sessionId = sessionMap.ResolvePrincipal(sessionId);
            }

            accessor.SessionId = sessionId;

            // Extract access token from HTTP Authorization header (enables TokenSource.Client for HTTP transport)
            var httpContextAccessor = services.GetService<IHttpContextAccessor>();
            var authHeader = httpContextAccessor?.HttpContext?.Request?.Headers["Authorization"].FirstOrDefault();
            if (!string.IsNullOrEmpty(authHeader))
            {
                accessor.AccessToken = authHeader;
            }
        }

        return await _innerTool.InvokeAsync(context, token);
    }
}