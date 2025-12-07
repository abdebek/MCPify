using MCPify.Core;
using MCPify.Core.Auth;
using MCPify.Core.Auth.OAuth;
using MCPify.Hosting;
using MCPify.Sample;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Use a specific port for the sample so we can hardcode URLs reliably in the demo
builder.WebHost.UseUrls("http://localhost:5005");

var transport = builder.Configuration.GetValue<McpTransportType>("Mcpify:Transport", McpTransportType.Stdio);
var enableOAuthDemo = builder.Configuration.GetValue("Demo:EnableOAuth", false);

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
        Filter = descriptor =>
            !descriptor.Route.StartsWith("/mock-auth") &&
            !descriptor.Route.StartsWith("/mock-api")
    };

    // 2. Mock Secure API (Demonstrates OAuth2 Flow) - opt-in
    if (enableOAuthDemo)
    {
        options.AddOAuthDemo("mock-api.json", "http://localhost:5005");
    }
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

if (enableOAuthDemo)
{
    app.MapOAuthDemoEndpoints(jwtIssuer, jwtAudience, jwtKey);
}

app.MapGet("/api/users/{id}", (int id) => new { Id = id, Name = $"User {id}" });
app.MapGet("/status", () => "MCPify Sample is Running");

// Register MCPify tools
var registrar = app.Services.GetRequiredService<McpifyServiceRegistrar>();
await registrar.RegisterToolsAsync(((IEndpointRouteBuilder)app).DataSources);

app.MapMcpifyEndpoint();

app.Run();

public record UserRequest(string Name, string Email);
