using System.Net;
using System.Text;
using System.Text.Json;
using MCPify.Core;
using MCPify.Core.Auth;
using MCPify.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MCPify.Tests.Integration;

public class OAuthMiddlewareTests
{
    [Fact]
    public async Task Request_Returns401_WhenNoToken_And_OAuthConfigured()
    {
        using var host = await CreateHostAsync(services =>
        {
            var store = services.GetRequiredService<OAuthConfigurationStore>();
            store.AddConfiguration(new OAuth2Configuration { AuthorizationUrl = "https://auth" });
        });
        
        var client = host.GetTestClient();

        var response = await client.GetAsync("/mcp");
        
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("WWW-Authenticate", response.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value)).Keys);
        var authHeader = response.Headers.WwwAuthenticate.ToString();
        Assert.Contains("resource_metadata", authHeader);
    }

    [Fact]
    public async Task Request_Challenge_UsesResourceOverride()
    {
        var publicUrl = "https://proxy.example.com";

        using var host = await CreateHostAsync(services =>
        {
            var store = services.GetRequiredService<OAuthConfigurationStore>();
            store.AddConfiguration(new OAuth2Configuration { AuthorizationUrl = "https://auth" });
        }, options =>
        {
            options.ResourceUrlOverride = publicUrl;
        });

        var client = host.GetTestClient();

        var response = await client.GetAsync("/mcp");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var authHeader = response.Headers.WwwAuthenticate.ToString();
        // Per MCP spec, WWW-Authenticate should contain resource_metadata URL
        Assert.Contains($"resource_metadata=\"{publicUrl}/.well-known/oauth-protected-resource\"", authHeader);
    }

    [Fact]
    public async Task Request_Challenge_IncludesScope_WhenConfigured()
    {
        using var host = await CreateHostAsync(services =>
        {
            var store = services.GetRequiredService<OAuthConfigurationStore>();
            store.AddConfiguration(new OAuth2Configuration
            {
                AuthorizationUrl = "https://auth",
                Scopes = new Dictionary<string, string>
                {
                    { "read", "Read access" },
                    { "write", "Write access" }
                }
            });
        });

        var client = host.GetTestClient();

        var response = await client.GetAsync("/mcp");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var authHeader = response.Headers.WwwAuthenticate.ToString();
        // Per MCP spec, WWW-Authenticate SHOULD include scope parameter
        Assert.Contains("scope=", authHeader);
        Assert.Contains("read", authHeader);
        Assert.Contains("write", authHeader);
    }

    [Fact]
    public async Task Request_Returns200_WhenTokenPresent()
    {
        using var host = await CreateHostAsync(services =>
        {
            var store = services.GetRequiredService<OAuthConfigurationStore>();
            store.AddConfiguration(new OAuth2Configuration { AuthorizationUrl = "https://auth" });
        });
        
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "token");

        var response = await client.GetAsync("/mcp");
        
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Request_Returns200_WhenNoOAuthConfigured()
    {
        using var host = await CreateHostAsync();
        var client = host.GetTestClient();

        var response = await client.GetAsync("/mcp");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Request_Returns401_WhenTokenExpired_AndValidationEnabled()
    {
        using var host = await CreateHostAsync(services =>
        {
            var store = services.GetRequiredService<OAuthConfigurationStore>();
            store.AddConfiguration(new OAuth2Configuration { AuthorizationUrl = "https://auth" });
        }, options =>
        {
            options.TokenValidation = new TokenValidationOptions
            {
                EnableJwtValidation = true,
                ClockSkew = TimeSpan.Zero
            };
        });

        var client = host.GetTestClient();

        // Create an expired JWT token
        var expiredToken = CreateJwt(new
        {
            sub = "user123",
            exp = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds()
        });
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", expiredToken);

        var response = await client.GetAsync("/mcp");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var authHeader = response.Headers.WwwAuthenticate.ToString();
        Assert.Contains("error=\"invalid_token\"", authHeader);
        Assert.Contains("expired", authHeader.ToLower());
    }

    [Fact]
    public async Task Request_Returns401_WhenTokenAudienceDoesNotMatch()
    {
        using var host = await CreateHostAsync(services =>
        {
            var store = services.GetRequiredService<OAuthConfigurationStore>();
            store.AddConfiguration(new OAuth2Configuration { AuthorizationUrl = "https://auth" });
        }, options =>
        {
            options.ResourceUrlOverride = "https://api.example.com";
            options.TokenValidation = new TokenValidationOptions
            {
                EnableJwtValidation = true,
                ValidateAudience = true
            };
        });

        var client = host.GetTestClient();

        // Create a JWT token with wrong audience
        var token = CreateJwt(new
        {
            sub = "user123",
            aud = "https://other-api.example.com",
            exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        });
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/mcp");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var authHeader = response.Headers.WwwAuthenticate.ToString();
        Assert.Contains("error=\"invalid_token\"", authHeader);
        Assert.Contains("audience", authHeader.ToLower());
    }

    [Fact]
    public async Task Request_Returns403_WhenTokenHasInsufficientScopes()
    {
        using var host = await CreateHostAsync(services =>
        {
            var store = services.GetRequiredService<OAuthConfigurationStore>();
            store.AddConfiguration(new OAuth2Configuration { AuthorizationUrl = "https://auth" });
        }, options =>
        {
            options.TokenValidation = new TokenValidationOptions
            {
                EnableJwtValidation = true,
                ValidateScopes = true,
                DefaultRequiredScopes = new List<string> { "mcp.access" }
            };
        });

        var client = host.GetTestClient();

        // Create a JWT token without required scope
        var token = CreateJwt(new
        {
            sub = "user123",
            scope = "read write",
            exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        });
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/mcp");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var authHeader = response.Headers.WwwAuthenticate.ToString();
        Assert.Contains("error=\"insufficient_scope\"", authHeader);
        Assert.Contains("mcp.access", authHeader);
    }

    [Fact]
    public async Task Request_Succeeds_WhenTokenHasRequiredScopes()
    {
        using var host = await CreateHostAsync(services =>
        {
            var store = services.GetRequiredService<OAuthConfigurationStore>();
            store.AddConfiguration(new OAuth2Configuration { AuthorizationUrl = "https://auth" });
        }, options =>
        {
            options.TokenValidation = new TokenValidationOptions
            {
                EnableJwtValidation = true,
                ValidateScopes = true,
                DefaultRequiredScopes = new List<string> { "mcp.access" }
            };
        });

        var client = host.GetTestClient();

        // Create a JWT token with required scope
        var token = CreateJwt(new
        {
            sub = "user123",
            scope = "mcp.access read write",
            exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        });
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/mcp");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Request_Succeeds_WhenTokenValidationDisabled()
    {
        using var host = await CreateHostAsync(services =>
        {
            var store = services.GetRequiredService<OAuthConfigurationStore>();
            store.AddConfiguration(new OAuth2Configuration { AuthorizationUrl = "https://auth" });
        });

        var client = host.GetTestClient();

        // Create an expired JWT token - should still work when validation is disabled
        var expiredToken = CreateJwt(new
        {
            sub = "user123",
            exp = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds()
        });
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", expiredToken);

        var response = await client.GetAsync("/mcp");

        // Token validation is disabled by default, so expired token should be accepted
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

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

    private async Task<IHost> CreateHostAsync(Action<IServiceProvider>? configure = null, Action<McpifyOptions>? configureOptions = null)
    {
        return await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddMcpify(options =>
                        {
                            configureOptions?.Invoke(options);
                        });
                        services.AddLogging();
                    })
                    .Configure(app =>
                    {
                        configure?.Invoke(app.ApplicationServices);
                        app.UseMcpifyOAuth();
                        app.Map("/mcp", b => b.Run(async c => 
                        {
                            c.Response.StatusCode = 200;
                            await c.Response.WriteAsync("OK");
                        }));
                    });
            })
            .StartAsync();
    }
}
