using System.Net;
using System.Text;
using System.Text.Json;
using MCPify.Core;
using MCPify.Core.Auth;
using MCPify.Hosting;
using MCPify.OpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;

namespace MCPify.Tests.Integration;

/// <summary>
/// Integration tests verifying that OAuth scopes from OpenAPI specs
/// are automatically enforced during token validation.
/// </summary>
public class OpenApiOAuthScopeIntegrationTests
{
    #region Test Helpers

    private static string CreateJwt(object payload)
    {
        var header = new { alg = "HS256", typ = "JWT" };
        var headerJson = JsonSerializer.Serialize(header);
        var payloadJson = JsonSerializer.Serialize(payload);

        var headerB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        var signatureB64 = Base64UrlEncode(Encoding.UTF8.GetBytes("dummy-signature"));

        return $"{headerB64}.{payloadB64}.{signatureB64}";
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    /// <summary>
    /// Creates an OpenAPI document with OAuth2 security scheme containing specified scopes.
    /// </summary>
    private static OpenApiDocument CreateOpenApiDocWithOAuth(params string[] scopes)
    {
        var scopeDict = scopes.ToDictionary(s => s, s => $"{s} access");

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

    #endregion

    [Fact]
    public void OpenApiParser_ExtractsScopes_AndAddsToStore()
    {
        // Arrange
        var parser = new OpenApiOAuthParser();
        var store = new OAuthConfigurationStore();
        var doc = CreateOpenApiDocWithOAuth("read", "write", "admin");

        // Act
        var config = parser.Parse(doc);
        if (config != null)
        {
            store.AddConfiguration(config);
        }

        // Assert
        Assert.NotNull(config);
        Assert.Equal(3, config.Scopes.Count);
        Assert.Contains("read", config.Scopes.Keys);
        Assert.Contains("write", config.Scopes.Keys);
        Assert.Contains("admin", config.Scopes.Keys);

        var storedConfigs = store.GetConfigurations().ToList();
        Assert.Single(storedConfigs);
        Assert.Equal(3, storedConfigs[0].Scopes.Count);
    }

    [Fact]
    public void ScopeRequirementStore_UsesOpenApiScopes_WhenRequireOAuthConfiguredScopesEnabled()
    {
        // Arrange
        var parser = new OpenApiOAuthParser();
        var oauthStore = new OAuthConfigurationStore();
        var doc = CreateOpenApiDocWithOAuth("api.read", "api.write");

        var config = parser.Parse(doc);
        oauthStore.AddConfiguration(config!);

        var options = new TokenValidationOptions
        {
            RequireOAuthConfiguredScopes = true
        };
        var scopeStore = new ScopeRequirementStore(new List<ScopeRequirement>(), options, oauthStore);

        // Act & Assert - Token with all scopes passes
        var validResult = scopeStore.ValidateScopesForTool("any_tool", new[] { "api.read", "api.write" });
        Assert.True(validResult.IsValid);

        // Token missing one scope fails
        var invalidResult = scopeStore.ValidateScopesForTool("any_tool", new[] { "api.read" });
        Assert.False(invalidResult.IsValid);
        Assert.Contains("api.write", invalidResult.MissingScopes);

        // Token with no scopes fails
        var emptyResult = scopeStore.ValidateScopesForTool("any_tool", Array.Empty<string>());
        Assert.False(emptyResult.IsValid);
        Assert.Contains("api.read", emptyResult.MissingScopes);
        Assert.Contains("api.write", emptyResult.MissingScopes);
    }

    [Fact]
    public async Task Middleware_Returns403_WhenTokenLacksOpenApiDefinedScopes()
    {
        // This test simulates the full flow:
        // 1. OAuth config with scopes is added to store (simulating OpenAPI parsing)
        // 2. Token validation is enabled with RequireOAuthConfiguredScopes
        // 3. Request with token missing scopes gets 403

        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddMcpify(options =>
                        {
                            options.TokenValidation = new TokenValidationOptions
                            {
                                EnableJwtValidation = true,
                                ValidateScopes = true,
                                RequireOAuthConfiguredScopes = true
                            };
                        });
                        services.AddLogging();
                    })
                    .Configure(app =>
                    {
                        // Simulate OAuth config from OpenAPI (normally done by McpifyServiceRegistrar)
                        var oauthStore = app.ApplicationServices.GetRequiredService<OAuthConfigurationStore>();
                        oauthStore.AddConfiguration(new OAuth2Configuration
                        {
                            AuthorizationUrl = "https://auth.example.com/authorize",
                            TokenUrl = "https://auth.example.com/token",
                            Scopes = new Dictionary<string, string>
                            {
                                { "api.read", "Read API" },
                                { "api.write", "Write API" }
                            }
                        });

                        app.UseMcpifyOAuth();
                        app.Map("/mcp", b => b.Run(async c =>
                        {
                            c.Response.StatusCode = 200;
                            await c.Response.WriteAsync("OK");
                        }));
                    });
            })
            .StartAsync();

        var client = host.GetTestClient();

        // Token with only 'api.read' scope - missing 'api.write'
        var token = CreateJwt(new
        {
            sub = "user123",
            scope = "api.read",
            exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        });
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/mcp");

        // Assert
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var authHeader = response.Headers.WwwAuthenticate.ToString();
        Assert.Contains("insufficient_scope", authHeader);
        Assert.Contains("api.write", authHeader);
    }

    [Fact]
    public async Task Middleware_Returns200_WhenTokenHasAllOpenApiDefinedScopes()
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddMcpify(options =>
                        {
                            options.TokenValidation = new TokenValidationOptions
                            {
                                EnableJwtValidation = true,
                                ValidateScopes = true,
                                RequireOAuthConfiguredScopes = true
                            };
                        });
                        services.AddLogging();
                    })
                    .Configure(app =>
                    {
                        // Simulate OAuth config from OpenAPI
                        var oauthStore = app.ApplicationServices.GetRequiredService<OAuthConfigurationStore>();
                        oauthStore.AddConfiguration(new OAuth2Configuration
                        {
                            AuthorizationUrl = "https://auth.example.com/authorize",
                            TokenUrl = "https://auth.example.com/token",
                            Scopes = new Dictionary<string, string>
                            {
                                { "api.read", "Read API" },
                                { "api.write", "Write API" }
                            }
                        });

                        app.UseMcpifyOAuth();
                        app.Map("/mcp", b => b.Run(async c =>
                        {
                            c.Response.StatusCode = 200;
                            await c.Response.WriteAsync("OK");
                        }));
                    });
            })
            .StartAsync();

        var client = host.GetTestClient();

        // Token with all required scopes
        var token = CreateJwt(new
        {
            sub = "user123",
            scope = "api.read api.write",
            exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        });
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/mcp");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Middleware_IgnoresOpenApiScopes_WhenRequireOAuthConfiguredScopesDisabled()
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddMcpify(options =>
                        {
                            options.TokenValidation = new TokenValidationOptions
                            {
                                EnableJwtValidation = true,
                                ValidateScopes = true,
                                RequireOAuthConfiguredScopes = false // Disabled
                            };
                        });
                        services.AddLogging();
                    })
                    .Configure(app =>
                    {
                        // OAuth config exists but RequireOAuthConfiguredScopes is false
                        var oauthStore = app.ApplicationServices.GetRequiredService<OAuthConfigurationStore>();
                        oauthStore.AddConfiguration(new OAuth2Configuration
                        {
                            AuthorizationUrl = "https://auth.example.com/authorize",
                            Scopes = new Dictionary<string, string>
                            {
                                { "api.read", "Read API" },
                                { "api.write", "Write API" }
                            }
                        });

                        app.UseMcpifyOAuth();
                        app.Map("/mcp", b => b.Run(async c =>
                        {
                            c.Response.StatusCode = 200;
                            await c.Response.WriteAsync("OK");
                        }));
                    });
            })
            .StartAsync();

        var client = host.GetTestClient();

        // Token with NO scopes - should still pass since RequireOAuthConfiguredScopes is false
        var token = CreateJwt(new
        {
            sub = "user123",
            exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        });
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await client.GetAsync("/mcp");

        // Assert - Should pass because OAuth scopes are not required
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Middleware_CombinesOpenApiScopes_WithDefaultRequiredScopes()
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddMcpify(options =>
                        {
                            options.TokenValidation = new TokenValidationOptions
                            {
                                EnableJwtValidation = true,
                                ValidateScopes = true,
                                RequireOAuthConfiguredScopes = true,
                                DefaultRequiredScopes = new List<string> { "mcp.access" } // Additional scope
                            };
                        });
                        services.AddLogging();
                    })
                    .Configure(app =>
                    {
                        var oauthStore = app.ApplicationServices.GetRequiredService<OAuthConfigurationStore>();
                        oauthStore.AddConfiguration(new OAuth2Configuration
                        {
                            AuthorizationUrl = "https://auth.example.com/authorize",
                            Scopes = new Dictionary<string, string>
                            {
                                { "api.read", "Read API" }
                            }
                        });

                        app.UseMcpifyOAuth();
                        app.Map("/mcp", b => b.Run(async c =>
                        {
                            c.Response.StatusCode = 200;
                            await c.Response.WriteAsync("OK");
                        }));
                    });
            })
            .StartAsync();

        var client = host.GetTestClient();

        // Token with OpenAPI scope but missing default scope
        var tokenMissingDefault = CreateJwt(new
        {
            sub = "user123",
            scope = "api.read",
            exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        });
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenMissingDefault);

        var response1 = await client.GetAsync("/mcp");
        Assert.Equal(HttpStatusCode.Forbidden, response1.StatusCode);
        Assert.Contains("mcp.access", response1.Headers.WwwAuthenticate.ToString());

        // Token with all scopes (OpenAPI + default)
        var tokenComplete = CreateJwt(new
        {
            sub = "user123",
            scope = "api.read mcp.access",
            exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        });
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenComplete);

        var response2 = await client.GetAsync("/mcp");
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
    }
}
