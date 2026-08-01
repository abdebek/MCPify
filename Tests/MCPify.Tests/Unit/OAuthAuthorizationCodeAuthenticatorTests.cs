using MCPify.Core;
using MCPify.Core.Auth;
using MCPify.Core.Auth.OAuth;
using MCPify.OpenApi;
using Xunit;

namespace MCPify.Tests.Unit;

public class OAuthAuthorizationCodeAuthenticatorTests
{
    [Fact]
    public async Task CreateAsync_UsesExplicitEndpoints()
    {
        var store = new InMemoryTokenStore();
        var accessor = new McpContextAccessor();

        var auth = await OAuthAuthorizationCodeAuthenticator.CreateAsync(
            new OAuthAuthorizationCodeAuthenticatorOptions
            {
                ClientId = "my-client",
                RedirectUri = "https://app.example.com/callback",
                StateSecret = "test-state-secret-must-be-32-chars-min!!",
                AuthorizationEndpoint = "https://auth.example.com/authorize",
                TokenEndpoint = "https://auth.example.com/token",
                Scope = "openid profile"
            },
            store,
            accessor);

        Assert.Contains("auth.example.com", auth.BuildAuthorizationUrl("s1"));
        Assert.Equal("OAuth:my-client@auth.example.com", auth.ProviderName);
    }

    [Fact]
    public async Task CreateAsync_LoadsAuthorizationCodeFromOpenApiFile()
    {
        var store = new InMemoryTokenStore();
        var accessor = new McpContextAccessor();
        var openApiPath = Path.Combine(Path.GetTempPath(), $"mcpify-oauth-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(openApiPath, """
            {
              "openapi": "3.0.3",
              "info": { "title": "t", "version": "1" },
              "paths": {},
              "components": {
                "securitySchemes": {
                  "oauth2": {
                    "type": "oauth2",
                    "flows": {
                      "authorizationCode": {
                        "authorizationUrl": "https://idp.example.com/authorize",
                        "tokenUrl": "https://idp.example.com/token",
                        "scopes": { "read": "Read", "write": "Write" }
                      }
                    }
                  }
                }
              }
            }
            """);

        try
        {
            var auth = await OAuthAuthorizationCodeAuthenticator.CreateAsync(
                new OAuthAuthorizationCodeAuthenticatorOptions
                {
                    ClientId = "from-openapi",
                    RedirectUri = "https://app.example.com/callback",
                    StateSecret = "test-state-secret-must-be-32-chars-min!!",
                    OpenApiFilePath = openApiPath
                },
                store,
                accessor,
                openApiProvider: new OpenApiV3Provider());

            var url = auth.BuildAuthorizationUrl("s1");
            Assert.Contains("idp.example.com", url);
            Assert.Contains("from-openapi", auth.ProviderName);
        }
        finally
        {
            File.Delete(openApiPath);
        }
    }
}
