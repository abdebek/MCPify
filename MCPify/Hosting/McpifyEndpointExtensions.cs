using MCPify.Core;
using MCPify.Core.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Server;
using System.Linq;

namespace MCPify.Hosting;

public static class McpifyEndpointExtensions
{
    public static IEndpointRouteBuilder MapMcpifyEndpoint(
        this IEndpointRouteBuilder app,
        string path = "")
    {
        var services = app.ServiceProvider;
        var options = services.GetService<McpifyOptions>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("McpifyEndpointExtensions");

        if (options == null)
        {
            logger.LogError("[MCPify] McpifyOptions not found. Cannot map MCP endpoints.");
            return app;
        }

        UpstreamAuthTransportPolicy.WarnIfNeeded(options, logger);

        // Single registration entry point: run the registrar for both local and external tools.
        // This replaces the old pattern of calling RegisterToolsAsync separately + having
        // MapMcpifyEndpoint re-register local endpoints (which lacked SessionAwareToolDecorator).
        var registrar = services.GetService<McpifyServiceRegistrar>();
        if (registrar != null)
        {
            try
            {
                registrar.RegisterToolsAsync(((IEndpointRouteBuilder)app).DataSources).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[MCPify] Error registering tools via McpifyServiceRegistrar.");
            }
        }
        else
        {
            logger.LogWarning("[MCPify] McpifyServiceRegistrar not found. Tools will not be registered.");
        }

        // Get OAuth store once for reuse
        var oauthStore = services.GetService<OAuthConfigurationStore>();

        if (options.Transport == McpTransportType.Http)
        {
            var mcpRoute = app.MapMcp(path);
            if (oauthStore?.GetConfigurations().Any() == true)
            {
                mcpRoute.RequireAuthorization(new AuthorizeAttribute
                {
                    AuthenticationSchemes = McpAuthenticationDefaults.AuthenticationScheme
                });
            }
        }

        // Note: /.well-known/oauth-protected-resource is served by the official
        // McpAuthenticationHandler via McpAuthenticationOptionsSetup.OnResourceMetadataRequest.
        // Do not map it manually here — that would create a duplicate route.

        return app;
    }
}
