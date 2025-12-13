using Microsoft.OpenApi.Models;
using MCPify.OpenApi;
using MCPify.Core.Auth;

namespace MCPify.Tests;

public class OpenApiOAuthParserTests
{
    [Fact]
    public void Parse_ReturnsNull_WhenNoSecuritySchemes()
    {
        var parser = new OpenApiOAuthParser();
        var doc = new OpenApiDocument(); // No Components
        
        var result = parser.Parse(doc);
        
        Assert.Null(result);
    }

    [Fact]
    public void Parse_ReturnsNull_WhenNoOAuth2Scheme()
    {
        var parser = new OpenApiOAuthParser();
        var doc = new OpenApiDocument
        {
            Components = new OpenApiComponents
            {
                SecuritySchemes = new Dictionary<string, OpenApiSecurityScheme>
                {
                    ["apiKey"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey }
                }
            }
        };

        var result = parser.Parse(doc);
        
        Assert.Null(result);
    }

    [Fact]
    public void Parse_ExtractsAuthorizationCodeFlow()
    {
        var parser = new OpenApiOAuthParser();
        var authUrl = "https://example.com/auth";
        var tokenUrl = "https://example.com/token";
        var refreshUrl = "https://example.com/refresh";
        
        var doc = new OpenApiDocument
        {
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
                                AuthorizationUrl = new Uri(authUrl),
                                TokenUrl = new Uri(tokenUrl),
                                RefreshUrl = new Uri(refreshUrl),
                                Scopes = new Dictionary<string, string>
                                {
                                    ["read"] = "Read access",
                                    ["write"] = "Write access"
                                }
                            }
                        }
                    }
                }
            }
        };

        var result = parser.Parse(doc);

        Assert.NotNull(result);
        Assert.Equal(authUrl, result.AuthorizationUrl);
        Assert.Equal(tokenUrl, result.TokenUrl);
        Assert.Equal(refreshUrl, result.RefreshUrl);
        Assert.Equal("authorization_code", result.FlowType);
        Assert.Equal(2, result.Scopes.Count);
        Assert.Equal("Read access", result.Scopes["read"]);
    }
}
