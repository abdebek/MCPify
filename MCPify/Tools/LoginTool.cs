using MCPify.Core;
using MCPify.Core.Auth.OAuth;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text.Json;

namespace MCPify.Tools;

public class LoginTool : McpServerTool
{
    public LoginTool()
    {
    }

    public override Tool ProtocolTool => new()
    {
        Name = "login_auth_code_pkce",
        Description = "Return an authorization URL for auth code + PKCE. Provide sessionId (optional, defaults to 'default'). Open the URL, approve the app, and the callback will store a token for that session.",
        InputSchema = JsonDocument.Parse("""{ "type": "object", "properties": { "sessionId": { "type": "string" } } }""").RootElement
    };

    public override IReadOnlyList<object> Metadata => Array.Empty<object>();

    public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> context, CancellationToken token)
    {
        string sessionId = Constants.DefaultSessionId;

        if (context.Params?.Arguments != null &&
            context.Params.Arguments.TryGetValue("sessionId", out var sessionElement) &&
            sessionElement.ValueKind == JsonValueKind.String &&
            !string.IsNullOrEmpty(sessionElement.GetString()))
        {
            sessionId = sessionElement.GetString()!;
        }

        var auth = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<OAuthAuthorizationCodeAuthentication>(context.Services!);

        var authUrl = auth.BuildAuthorizationUrl(sessionId);

        var response = $"auth_url: {authUrl}";

        return ValueTask.FromResult(new CallToolResult
        {
            Content = new[] { new TextContentBlock { Text = response } }
        });
    }

    private static CallToolResult Error(string message) => new()
    {
        IsError = true,
        Content = new[] { new TextContentBlock { Text = message } }
    };
}