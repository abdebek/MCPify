using System;
using System.Net;
using System.Net.Http.Headers;
using System.Linq;
using System.Text;
using MCPify.Core;
using MCPify.Core.Auth;
using MCPify.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MCPify.Tests.Integration;

public class OAuthChallengeTokenValidationTests
{
    [Fact]
    public async Task PostWithoutSession_ReturnsUnauthorizedChallenge_WhenAuthenticationRequired()
    {
        await using var host = await CreateHostAsync();
        var client = host.GetTestClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        var payload = """
        {
            "jsonrpc": "2.0",
            "method": "ping",
            "params": {},
            "id": 1
        }
        """;
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        var authenticateHeader = string.Join(" | ", response.Headers.WwwAuthenticate.Select(h => h.ToString()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(response.Headers.WwwAuthenticate, header =>
            string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("resource_metadata", authenticateHeader);
        Assert.True(body.Length == 0, $"Expected empty body but received: {body}");
    }

    private static async Task<WebApplication> CreateHostAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddLogging();
        builder.Services.AddAuthorization();
        builder.Services.AddMcpify(options =>
        {
            options.Transport = McpTransportType.Http;
            options.OAuthConfigurations.Add(new OAuth2Configuration
            {
                AuthorizationUrl = "https://auth.example.com/authorize",
                TokenUrl = "https://auth.example.com/token",
                Scopes = new Dictionary<string, string>
                {
                    { "read", "Read access" }
                }
            });
        });

        // Opt in to MCP as the default auth scheme (host app's responsibility, not the library's).
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
