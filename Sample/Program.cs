using MCPify.Core;
using MCPify.Core.Auth;
using MCPify.Core.Auth.OAuth;
using MCPify.Hosting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Routing;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var transport = builder.Configuration.GetValue<McpTransportType>("Mcpify:Transport", McpTransportType.Stdio);

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
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
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

// Generate a demo token for the MCPify client
var tokenHandler = new JwtSecurityTokenHandler();
var tokenDescriptor = new SecurityTokenDescriptor
{
    Subject = new ClaimsIdentity(new[] { new Claim("sub", "mcp-agent"), new Claim(ClaimTypes.Name, "MCP Agent") }),
    Expires = DateTime.UtcNow.AddYears(1),
    Issuer = jwtIssuer,
    Audience = jwtAudience,
    SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)), SecurityAlgorithms.HmacSha256Signature)
};
var demoToken = tokenHandler.WriteToken(tokenHandler.CreateToken(tokenDescriptor));

Console.WriteLine($"[Auth] Generated Bearer Token for MCPify: {demoToken}");
// -------------------------

builder.Services.AddMcpifyTestTool();

builder.Services.AddMcpify(options =>
{
    options.Transport = transport;

    // Configure local endpoints with the generated Bearer token
    options.LocalEndpoints = new()
    {
        Enabled = true,
        ToolPrefix = "local_",
        Authentication = new BearerAuthentication(demoToken)
    };

    options.ExternalApis.Add(new()
    {
        SwaggerUrl = "https://petstore.swagger.io/v2/swagger.json",
        ApiBaseUrl = "https://petstore.swagger.io/v2",
        ToolPrefix = "petstore_",
        // Authentication = new ApiKeyAuthentication("api_key", "special-key", ApiKeyLocation.Header)
    });

    if (File.Exists("sample-api.json"))
    {
        options.ExternalApis.Add(new()
        {
            SwaggerFilePath = "sample-api.json",
            ApiBaseUrl = "http://localhost:5000",
            ToolPrefix = "file_"
        });
    }

    // Example: Connect to GitHub API using OAuth2 (Device Flow or Authorization Code)
    // NOTE: This requires a real GitHub Client ID/Secret to work. 
    // We are adding it as a commented-out example for the user to enable.
    /*
    options.ExternalApis.Add(new()
    {
        // GitHub doesn't publish a standard Swagger JSON, but for demo purposes, 
        // imagine we have one or point to a proxy. 
        // Real-world usage would point to your own API's swagger.
        SwaggerUrl = "https://api.github.com/openapi", // Hypothetical
        ApiBaseUrl = "https://api.github.com",
        ToolPrefix = "github_",
        Authentication = new OAuthAuthorizationCodeAuthentication(
            clientId: "YOUR_GITHUB_CLIENT_ID",
            clientSecret: "YOUR_GITHUB_CLIENT_SECRET",
            authorizationEndpoint: "https://github.com/login/oauth/authorize",
            tokenEndpoint: "https://github.com/login/oauth/access_token",
            scope: "repo user",
            tokenStore: new FileTokenStore("github_token.json")
        )
    });
    */
});

var app = builder.Build();

if (transport != McpTransportType.Stdio)
{
    app.Use(async (context, next) =>
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {context.Request.Method} {context.Request.Path}");
        await next();
    });
}

app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/users/{id}", (int id) => new { Id = id, Name = $"User {id}" });
app.MapPost("/api/users", (UserRequest user) => new { Id = 123, user.Name, user.Email });
app.MapGet("/status", () => "MCPify Sample is Running");

// Protected Endpoint
app.MapGet("/api/secure", (ClaimsPrincipal user) => 
    new {
        Message = "You have accessed a secure endpoint!", 
        User = user.Identity?.Name,
        Authenticated = true 
    })
    .RequireAuthorization()
    .WithOpenApi(operation => new(operation) { Summary = "Access a secure endpoint using JWT" }); // Ensure it has a summary for MCP tool description

// Register MCPify tools after endpoints are mapped but before MCP endpoint is mapped
var registrar = app.Services.GetRequiredService<McpifyServiceRegistrar>();
await registrar.RegisterToolsAsync(((IEndpointRouteBuilder)app).DataSources);

app.MapMcpifyEndpoint();

app.Run();

public record UserRequest(string Name, string Email);