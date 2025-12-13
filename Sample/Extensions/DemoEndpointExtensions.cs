using MCPify.Hosting;
using MCPify.Sample.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace MCPify.Sample.Extensions;

public static class DemoEndpointExtensions
{
    public static WebApplication MapDemoEndpoints(this WebApplication app, string oauthRedirectPath)
    {
        app.MapControllers();

        app.MapGet("/api/users/{id}", (int id) => new { Id = id, Name = $"User {id}" })
           .WithOpenApi(operation => new(operation) { Summary = "Get a user by ID" });

        app.MapGet("/api/secrets", (ClaimsPrincipal user) => 
            new { Secret = "The Golden Eagle flies at midnight.", Viewer = user.Identity?.Name ?? "Anonymous" })
           .RequireAuthorization()
           .WithOpenApi(operation => new(operation) { Summary = "Get top secrets (Secure)" });

        app.MapGet("/status", () => "MCPify Sample is Running");

        // Demo endpoint for localfile_getWeather tool
        app.MapGet("/weather", () => new { Temperature = 25, Condition = "Sunny" });

        app.MapAuthCallback(oauthRedirectPath);

        return app;
    }

    public static async Task RegisterDemoMcpToolsAsync(this WebApplication app)
    {
        var registrar = app.Services.GetRequiredService<McpifyServiceRegistrar>();
        await registrar.RegisterToolsAsync(((IEndpointRouteBuilder)app).DataSources);
    }
}
