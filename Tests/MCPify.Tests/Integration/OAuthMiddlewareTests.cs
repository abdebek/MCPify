using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MCPify.Core;
using MCPify.Core.Auth;
using MCPify.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace MCPify.Tests.Integration;

public class OAuthMiddlewareTests
{
    private const string JsonRpcContent = """
    {
        "jsonrpc": "2.0",
        "method": "ping",
        "params": {},
        "id": 1
    }
    """;

    [Fact]
    public async Task Request_Returns401_WhenNoToken_And_OAuthConfigured()
    {
        await using var app = await CreateHostAsync(options =>
        {
            options.OAuthConfigurations.Add(new OAuth2Configuration
            {
                AuthorizationUrl = "https://auth.example.com/authorize",
                TokenUrl = "https://auth.example.com/token",
                Scopes = new Dictionary<string, string> { { "read", "Read" } }
            });
        });

        var client = app.GetTestClient();

        var metadataPayload = await client.GetStringAsync("/.well-known/oauth-protected-resource");
        using var metadata = JsonDocument.Parse(metadataPayload);
        Assert.Equal("http://localhost", metadata.RootElement.GetProperty("resource").GetString());
        var scopes = metadata.RootElement.GetProperty("scopes_supported").EnumerateArray().Select(x => x.GetString()).Where(x => x != null).ToList();
        Assert.Contains("read", scopes);

        using var request = CreateJsonRpcRequest();
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var authenticateHeader = string.Join(" ", response.Headers.WwwAuthenticate.Select(h => h.ToString()));
        Assert.Contains("resource_metadata", authenticateHeader);
        // RFC 6750 / MCP Authorization: SHOULD include scope on WWW-Authenticate
        Assert.Contains("scope=", authenticateHeader, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("read", authenticateHeader, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Request_Challenge_UsesResourceOverride()
    {
        const string publicUrl = "https://proxy.example.com";

        await using var app = await CreateHostAsync(options =>
        {
            options.ResourceUrlOverride = publicUrl;
            options.OAuthConfigurations.Add(new OAuth2Configuration
            {
                AuthorizationUrl = "https://auth.example.com/authorize",
                TokenUrl = "https://auth.example.com/token"
            });
        });

        var client = app.GetTestClient();
        var metadataPayload = await client.GetStringAsync("/.well-known/oauth-protected-resource");
        using var metadata = JsonDocument.Parse(metadataPayload);
        Assert.Equal(publicUrl, metadata.RootElement.GetProperty("resource").GetString());

        using var request = CreateJsonRpcRequest();
        var response = await client.SendAsync(request);
        var authenticateHeader = string.Join(" ", response.Headers.WwwAuthenticate.Select(h => h.ToString()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("resource_metadata=\"http://localhost/.well-known/oauth-protected-resource", authenticateHeader);
    }

    [Fact]
    public async Task Request_Challenge_IncludesScope_WhenConfigured()
    {
        await using var app = await CreateHostAsync(options =>
        {
            options.OAuthConfigurations.Add(new OAuth2Configuration
            {
                AuthorizationUrl = "https://auth.example.com/authorize",
                TokenUrl = "https://auth.example.com/token",
                Scopes = new Dictionary<string, string>
                {
                    { "read", "Read" },
                    { "write", "Write" }
                }
            });
        });

        using var request = CreateJsonRpcRequest();
        var response = await app.GetTestClient().SendAsync(request);
        var authenticateHeader = string.Join(" ", response.Headers.WwwAuthenticate.Select(h => h.ToString()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("resource_metadata=\"http://localhost/.well-known/oauth-protected-resource", authenticateHeader);

        var metadataPayload = await app.GetTestClient().GetStringAsync("/.well-known/oauth-protected-resource");
        using var metadata = JsonDocument.Parse(metadataPayload);
        var scopes = metadata.RootElement.GetProperty("scopes_supported").EnumerateArray().Select(x => x.GetString()).Where(x => x != null).ToList();
        Assert.Contains("read", scopes);
        Assert.Contains("write", scopes);
    }

    [Fact]
    public async Task Request_Returns200_WhenTokenPresent()
    {
        await using var app = await CreateHostAsync(options =>
        {
            options.OAuthConfigurations.Add(new OAuth2Configuration
            {
                AuthorizationUrl = "https://auth.example.com/authorize",
                TokenUrl = "https://auth.example.com/token"
            });
        });

        using var request = CreateJsonRpcRequest();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "token");
        var response = await app.GetTestClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var authenticateHeader = string.Join(" ", response.Headers.WwwAuthenticate.Select(h => h.ToString()));
        Assert.Contains("resource_metadata=\"http://localhost/.well-known/oauth-protected-resource", authenticateHeader);
    }

    [Fact]
    public async Task Request_Returns200_WhenNoOAuthConfigured()
    {
        await using var app = await CreateHostAsync();

        using var request = CreateJsonRpcRequest();
        var response = await app.GetTestClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static HttpRequestMessage CreateJsonRpcRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = new StringContent(JsonRpcContent, Encoding.UTF8, "application/json");
        return request;
    }

    private static async Task<WebApplication> CreateHostAsync(Action<McpifyOptions>? configureOptions = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddLogging();
        builder.Services.AddAuthorization();
        builder.Services.AddMcpify(options =>
        {
            options.Transport = McpTransportType.Http;
            configureOptions?.Invoke(options);
        });

        // These tests exercise the MCP auth scheme, so opt in to it as the default.
        // Host apps would do the same only if MCP is their primary auth; otherwise they
        // reference the scheme by name on the MCP route.
        builder.Services.PostConfigure<Microsoft.AspNetCore.Authentication.AuthenticationOptions>(o =>
        {
            o.DefaultScheme = ModelContextProtocol.AspNetCore.Authentication.McpAuthenticationDefaults.AuthenticationScheme;
            o.DefaultAuthenticateScheme = ModelContextProtocol.AspNetCore.Authentication.McpAuthenticationDefaults.AuthenticationScheme;
            o.DefaultChallengeScheme = ModelContextProtocol.AspNetCore.Authentication.McpAuthenticationDefaults.AuthenticationScheme;
        });

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapMcpifyEndpoint("/mcp");

        await app.StartAsync();
        return app;
    }
}
