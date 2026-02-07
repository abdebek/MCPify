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
    public override IReadOnlyList<object> Metadata => _innerTool.Metadata;

    public override async ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> context, CancellationToken token)
    {
        if (_innerTool.ProtocolTool.Name.Equals("connect", StringComparison.OrdinalIgnoreCase))
        {
            return await _innerTool.InvokeAsync(context, token);
        }

        var services = context.Services ?? _serviceProvider;
        var accessor = services.GetService<IMcpContextAccessor>();

        if (accessor == null)
        {
            return await _innerTool.InvokeAsync(context, token);
        }

        var previousSessionId = accessor.SessionId;
        var previousConnectionId = accessor.ConnectionId;
        var previousAccessToken = accessor.AccessToken;

        try
        {
            var sessionId = context.Server?.SessionId;

            if (string.IsNullOrEmpty(sessionId) && context.Params?.Arguments != null)
            {
                var argEntry = context.Params.Arguments.FirstOrDefault(x => x.Key.Equals("sessionId", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(argEntry.Key))
                {
                    if (argEntry.Value.ValueKind == JsonValueKind.String)
                    {
                        sessionId = argEntry.Value.GetString();
                    }
                    else
                    {
                        sessionId = argEntry.Value.ToString();
                    }
                }
            }

            var sessionMap = services.GetService<ISessionMap>();
            if (sessionMap != null && !string.IsNullOrEmpty(sessionId))
            {
                sessionId = sessionMap.ResolvePrincipal(sessionId);
            }

            accessor.SessionId = sessionId;

            var httpContextAccessor = services.GetService<IHttpContextAccessor>();
            var authHeader = httpContextAccessor?.HttpContext?.Request?.Headers["Authorization"].FirstOrDefault();
            accessor.AccessToken = string.IsNullOrEmpty(authHeader) ? null : authHeader;

            return await _innerTool.InvokeAsync(context, token);
        }
        finally
        {
            accessor.SessionId = previousSessionId;
            accessor.ConnectionId = previousConnectionId;
            accessor.AccessToken = previousAccessToken;
        }
    }
}
