using MCPify.Core;
using MCPify.Core.Auth.OAuth;
using MCPify.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace MCPify.Sample;

public static class OAuthDemoExtensions
{
    public static void AddOAuthDemo(this McpifyOptions options, string swaggerPath, string apiBaseUrl)
    {
        var mockSwaggerJson = """
        {
          "openapi": "3.0.1",
          "info": {
            "title": "Mock Secure API",
            "version": "1.0"
          },
          "paths": {
            "/mock-api/secrets": {
              "get": {
                "summary": "Get Top Secret Data",
                "description": "Requires OAuth2 Authentication",
                "operationId": "getSecrets",
                "responses": {
                  "200": {
                    "description": "Success",
                    "content": {
                      "application/json": {
                        "schema": {
                          "type": "object",
                          "properties": {
                            "secret": { "type": "string" },
                            "viewer": { "type": "string" }
                          }
                        }
                      }
                    }
                  }
                }
              }
            }
          }
        }
        """;

        File.WriteAllText(swaggerPath, mockSwaggerJson);

        options.ExternalApis.Add(new()
        {
            SwaggerFilePath = swaggerPath,
            ApiBaseUrl = apiBaseUrl,
            ToolPrefix = "secure_",
            Authentication = new OAuthAuthorizationCodeAuthentication(
                clientId: "demo-client-id",
                clientSecret: "demo-client-secret",
                authorizationEndpoint: $"{apiBaseUrl}/mock-auth/authorize",
                tokenEndpoint: $"{apiBaseUrl}/mock-auth/token",
                scope: "read_secrets",
                tokenStore: new FileTokenStore("demo_token.json")
            )
        });
    }

    public static void MapOAuthDemoEndpoints(this WebApplication app, string issuer, string audience, string signingKey)
    {
        app.MapGet("/mock-auth/authorize", (string response_type, string client_id, string redirect_uri, string state, string scope) =>
        {
            var html = $"""
            <html>
            <head><title>Mock Login</title></head>
            <body style="font-family: sans-serif; text-align: center; padding-top: 50px;">
                <h1>MCPify Mock Identity Provider</h1>
                <p>App <strong>{client_id}</strong> is requesting access.</p>
                <p>Scope: <code>{scope}</code></p>
                <form action="/mock-auth/approve" method="post">
                    <input type="hidden" name="redirect_uri" value="{redirect_uri}" />
                    <input type="hidden" name="state" value="{state}" />
                    <button type="submit" style="padding: 10px 20px; font-size: 16px; cursor: pointer; background-color: #0078d4; color: white; border: none; border-radius: 4px;">
                        Approve & Login
                    </button>
                </form>
            </body>
            </html>
            """;
            return Results.Content(html, "text/html");
        });

        app.MapPost("/mock-auth/approve", ([FromForm] string redirect_uri, [FromForm] string state) =>
        {
            var code = "mock_auth_code_" + Guid.NewGuid();
            return Results.Redirect($"{redirect_uri}?code={code}&state={state}");
        })
        .DisableAntiforgery();

        app.MapPost("/mock-auth/token", ([FromForm] string code) =>
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Demo User"), new Claim("sub", "demo_user_1") }),
                Expires = DateTime.UtcNow.AddHours(1),
                Issuer = issuer,
                Audience = audience,
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256Signature)
            };
            var accessToken = tokenHandler.WriteToken(tokenHandler.CreateToken(tokenDescriptor));

            return Results.Ok(new
            {
                access_token = accessToken,
                token_type = "Bearer",
                expires_in = 3600,
                refresh_token = "mock_refresh_token"
            });
        })
        .DisableAntiforgery();
    }
}
