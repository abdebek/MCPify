using MCPify.Core;
using MCPify.Core.Auth;
using MCPify.Core.Auth.OAuth;
using MCPify.Hosting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

// --- 0. Setup Mock OpenAPI Spec (Self-Contained Demo) ---
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
File.WriteAllText("mock-api.json", mockSwaggerJson);
// ---------------------------------------------------------

var builder = WebApplication.CreateBuilder(args);

// Use a specific port for the sample so we can hardcode URLs reliably in the demo
builder.WebHost.UseUrls("http://localhost:5005");

var transport = builder.Configuration.GetValue<McpTransportType>("Mcpify:Transport", McpTransportType.Stdio);

// Only clear logging if in Stdio mode and NOT debugging, to avoid corrupting stdout
if (transport == McpTransportType.Stdio && !args.Contains("--debug"))
{
    builder.Logging.ClearProviders();
}
else
{
    builder.Services.AddLogging();
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.SetIsOriginAllowed(_ => true).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    });
});

// --- JWT Configuration ---
var jwtKey = "ThisIsASecureKeyForTestingMCPifySamples_123!";
var jwtIssuer = "mcpify-sample";
var jwtAudience = "mcpify-client";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddAntiforgery();
builder.Services.AddMcpifyTestTool();

builder.Services.AddMcpify(options =>
{
    options.Transport = transport;

    // 1. Local Endpoints (Minimal APIs)
    options.LocalEndpoints = new()
    {
        Enabled = true,
        ToolPrefix = "local_",
        Filter = (descriptor) => 
            !descriptor.Route.StartsWith("/mock-auth") && 
            !descriptor.Route.StartsWith("/mock-api")
    };

    // 2. Mock Secure API (Demonstrates OAuth2 Flow)
    options.ExternalApis.Add(new()
    {
        SwaggerFilePath = "mock-api.json",
        ApiBaseUrl = "http://localhost:5005", // Points to this app itself
        ToolPrefix = "secure_",
        Authentication = new OAuthAuthorizationCodeAuthentication(
            clientId: "demo-client-id",
            clientSecret: "demo-client-secret",
            // The auth endpoints are hosted in this same app below
            authorizationEndpoint: "http://localhost:5005/mock-auth/authorize",
            tokenEndpoint: "http://localhost:5005/mock-auth/token",
            scope: "read_secrets",
            tokenStore: new FileTokenStore("demo_token.json") 
        )
    });
});

var app = builder.Build();

app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

// --- Mock API Endpoints ---
app.MapGet("/mock-api/secrets", (ClaimsPrincipal user) => 
    new { Secret = "The Golden Eagle flies at midnight.", Viewer = user.Identity?.Name ?? "Anonymous" })
   .RequireAuthorization();

// --- Mock OAuth2 Provider Endpoints ---
// 1. Authorize: Renders a login page (simulated)
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

// 2. Approve: Handles the form post and redirects back to the client
app.MapPost("/mock-auth/approve", ([FromForm] string redirect_uri, [FromForm] string state) =>
{
    var code = "mock_auth_code_" + Guid.NewGuid();
    return Results.Redirect($"{redirect_uri}?code={code}&state={state}");
})
.DisableAntiforgery();

// 3. Token: Exchanges code for JWT
app.MapPost("/mock-auth/token", ([FromForm] string code) =>
{
    // Mint a JWT
    var tokenHandler = new JwtSecurityTokenHandler();
    var tokenDescriptor = new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "Demo User"), new Claim("sub", "demo_user_1") }),
        Expires = DateTime.UtcNow.AddHours(1),
        Issuer = jwtIssuer,
        Audience = jwtAudience,
        SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)), SecurityAlgorithms.HmacSha256Signature)
    };
    var accessToken = tokenHandler.WriteToken(tokenHandler.CreateToken(tokenDescriptor));

    return Results.Ok(new
    {
        access_token = accessToken,
        token_type = "Bearer",
        expires_in = 3600,
        refresh_token = "mock_refresh_token"
    });
});
// -----------------------------

app.MapGet("/api/users/{id}", (int id) => new { Id = id, Name = $"User {id}" });
app.MapGet("/status", () => "MCPify Sample is Running");

// Register MCPify tools
var registrar = app.Services.GetRequiredService<McpifyServiceRegistrar>();
await registrar.RegisterToolsAsync(((IEndpointRouteBuilder)app).DataSources);

app.MapMcpifyEndpoint();

app.Run();

public record UserRequest(string Name, string Email);
