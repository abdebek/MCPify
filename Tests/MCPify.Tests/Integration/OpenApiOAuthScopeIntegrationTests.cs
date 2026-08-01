using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MCPify.Core;
using MCPify.Core.Auth;
using MCPify.Hosting;
using MCPify.OpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;

namespace MCPify.Tests.Integration;

public class OpenApiOAuthScopeIntegrationTests
{
    [Fact]
    public void OpenApiParser_ExtractsScopes_AndAddsToStore()
    {
        var parser = new OpenApiOAuthParser();
        var store = new OAuthConfigurationStore();
        var doc = CreateOpenApiDocWithOAuth("read", "write", "admin");

        var config = parser.Parse(doc);
        if (config != null)
        {
            store.AddConfiguration(config);
        }

        Assert.NotNull(config);
        Assert.Equal(3, config!.Scopes.Count);
        Assert.Contains("read", config.Scopes.Keys);
        Assert.Contains("write", config.Scopes.Keys);
        Assert.Contains("admin", config.Scopes.Keys);

        var stored = store.GetConfigurations().ToList();
        Assert.Single(stored);
        Assert.Equal(3, stored[0].Scopes.Count);
    }

    [Fact]
    public async Task MetadataEndpoint_ReturnsScopes_FromOpenApiConfiguration()
    {
        var parser = new OpenApiOAuthParser();
        var config = parser.Parse(CreateOpenApiDocWithOAuth("api.read", "api.write"));
        Assert.NotNull(config);

        await using var app = await CreateHostAsync(options =>
        {
            options.OAuthConfigurations.Add(config!);
        });

        var client = app.GetTestClient();
        var response = await client.GetAsync("/.well-known/oauth-protected-resource");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var scopes = payload.RootElement.GetProperty("scopes_supported").EnumerateArray()
            .Select(element => element.GetString())
            .Where(scope => scope != null)
            .ToList();

        Assert.Contains("api.read", scopes);
        Assert.Contains("api.write", scopes);
    }

    [Fact]
    public async Task Challenge_ListsScopes_FromOpenApiConfiguration()
    {
        var parser = new OpenApiOAuthParser();
        var config = parser.Parse(CreateOpenApiDocWithOAuth("api.read", "api.write"));
        Assert.NotNull(config);

        await using var app = await CreateHostAsync(options =>
        {
            options.OAuthConfigurations.Add(config!);
        });

        using var request = CreateJsonRpcRequest();
        var response = await app.GetTestClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var header = string.Join(" ", response.Headers.WwwAuthenticate.Select(h => h.ToString()));
        Assert.Contains("resource_metadata=\"http://localhost/.well-known/oauth-protected-resource", header);

        var metadataPayload = await app.GetTestClient().GetStringAsync("/.well-known/oauth-protected-resource");
        using var metadata = JsonDocument.Parse(metadataPayload);
        var scopes = metadata.RootElement.GetProperty("scopes_supported").EnumerateArray()
            .Select(element => element.GetString())
            .Where(scope => scope != null)
            .ToList();

        Assert.Contains("api.read", scopes);
        Assert.Contains("api.write", scopes);
    }

    private static HttpRequestMessage CreateJsonRpcRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = new StringContent("""
        {
            "jsonrpc": "2.0",
            "method": "ping",
            "params": {},
            "id": 1
        }
        """, Encoding.UTF8, "application/json");
        return request;
    }

    private static OpenApiDocument CreateOpenApiDocWithOAuth(params string[] scopes)
    {
        var scopeDict = scopes.ToDictionary(scope => scope, scope => $"{scope} access");

        return new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "Test API", Version = "1.0" },
            Paths = new OpenApiPaths
            {
                ["/test"] = new OpenApiPathItem
                {
                    Operations = new Dictionary<OperationType, OpenApiOperation>
                    {
                        [OperationType.Get] = new OpenApiOperation
                        {
                            OperationId = "getTest",
                            Responses = new OpenApiResponses
                            {
                                ["200"] = new OpenApiResponse { Description = "OK" }
                            }
                        }
                    }
                }
            },
            Components = new OpenApiComponents
            {
                SecuritySchemes = new Dictionary<string, OpenApiSecurityScheme>
                {
                    ["oauth2"] = new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.OAuth2,
                        Flows = new OpenApiOAuthFlows
                        {
                            AuthorizationCode = new OpenApiOAuthFlow
                            {
                                AuthorizationUrl = new Uri("https://auth.example.com/authorize"),
                                TokenUrl = new Uri("https://auth.example.com/token"),
                                Scopes = scopeDict
                            }
                        }
                    }
                }
            }
        };
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
