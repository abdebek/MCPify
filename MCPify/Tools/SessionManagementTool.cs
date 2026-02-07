using MCPify.Core;
using MCPify.Core.Session;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Security.Cryptography;
using System.Text.Json;

namespace MCPify.Tools;

public class SessionManagementTool : McpServerTool
{
    public override Tool ProtocolTool => new()
    {
        Name = "connect",
        Description = "Establish a new session with the server. Returns a unique Session ID. You MUST provide this Session ID in the 'sessionId' argument for ALL subsequent tool calls (e.g., login, fetch_secret) to maintain your authentication state.",
        InputSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {}
        }
        """).RootElement
    };

    public override IReadOnlyList<object> Metadata => Array.Empty<object>();

    public override async ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> context, CancellationToken token)
    {
        var accessor = context.Services?.GetService<IMcpContextAccessor>();
        var sessionMap = context.Services?.GetService<ISessionMap>();

        var sessionId = context.Server?.SessionId;

        if (string.IsNullOrEmpty(sessionId))
        {
            sessionId = CreateSessionId();
        }

        if (sessionMap != null)
        {
            sessionMap.UpgradeSession(Constants.DefaultSessionId, sessionId);
        }

        if (accessor != null)
        {
            accessor.SessionId = sessionId;
        }

        return new CallToolResult
        {
            Content = new[] { new TextContentBlock { Text = sessionId } }
        };
    }

    private static CallToolResult Error(string message) => new()
    {
        IsError = true,
        Content = new[] { new TextContentBlock { Text = message } }
    };

    private static string CreateSessionId()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
