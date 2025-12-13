using MCPify.Core.Auth;
using MCPify.Core.Auth.OAuth;
using MCPify.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Server;

namespace MCPify.Sample.Auth;

public static class AuthSetupExtensions
{


    public static IServiceCollection AddLoginTool(this IServiceCollection services, Func<IServiceProvider, LoginTool> factory)
    {
        services.AddSingleton<McpServerTool>(sp => factory(sp));
        return services;
    }

    public static void MapAuthCallback(this WebApplication app, string callbackPath, params string[] alternatePaths)
    {
        var paths = new List<string> { callbackPath };
        paths.AddRange(alternatePaths ?? Array.Empty<string>());

        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            app.MapGet(path, async context =>
            {
                var code = context.Request.Query["code"].ToString();
                var state = context.Request.Query["state"].ToString();

                if (string.IsNullOrEmpty(code))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsync("Missing code");
                    return;
                }

                if (string.IsNullOrEmpty(state))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsync("Missing state");
                    return;
                }

                var auth = context.RequestServices.GetRequiredService<OAuthAuthorizationCodeAuthentication>();

                try
                {
                    // The HandleAuthorizationCallbackAsync now takes the signed state and extracts sessionId
                    await auth.HandleAuthorizationCallbackAsync(code, state, context.RequestAborted);
                    await context.Response.WriteAsync("Login complete. You can close this window and return to your MCP client.");
                }
                catch (Exception ex)
                {
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                    await context.Response.WriteAsync($"Auth exchange failed: {ex.Message}");
                }
            });
        }
    }
}
