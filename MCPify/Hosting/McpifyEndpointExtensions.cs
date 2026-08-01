using MCPify.Core;
using MCPify.Core.Auth;
using MCPify.Core.Auth.TokenProviders;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Server;
using MCPify.Endpoints;
using MCPify.Tools;
using MCPify.Schema;
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

        if (options.LocalEndpoints?.Enabled == true)
        {
            try
            {
                var endpointProvider = services.GetRequiredService<IEndpointMetadataProvider>() as AspNetCoreEndpointMetadataProvider;
                if (endpointProvider == null)
                {
                    logger.LogError("[MCPify] AspNetCoreEndpointMetadataProvider not found for local endpoints.");
                }
                else
                {
                    var toolCollection = services.GetService<McpServerPrimitiveCollection<McpServerTool>>();
                    if (toolCollection == null)
                    {
                         logger.LogWarning("[MCPify] McpServerPrimitiveCollection not found. Local endpoints cannot be registered.");
                    }
                    else
                    {
                        var operations = endpointProvider.GetLocalEndpoints().ToList();
                        logger.LogInformation($"[MCPify] AspNetCoreEndpointMetadataProvider found {operations.Count} raw local operations.");

                        if (options.LocalEndpoints!.Filter != null)
                        {
                            operations = operations.Where(options.LocalEndpoints.Filter).ToList();
                        }
                        logger.LogInformation($"[MCPify] After local endpoint filter, {operations.Count} operations remaining.");

                        var httpClient = services.GetRequiredService<IHttpClientFactory>().CreateClient();

                        string BaseUrlProvider()
                        {
                            var server = services.GetService<IServer>();
                            var addresses = server?.Features.Get<IServerAddressesFeature>()?.Addresses;
                            var baseUrl = options.LocalEndpoints?.BaseUrlOverride
                                ?? addresses?.FirstOrDefault()
                                ?? Constants.DefaultBaseUrl;
                            logger.LogDebug($"[MCPify] BaseUrlProvider returning: {baseUrl}");
                            return baseUrl;
                        }

                        var count = 0;
                        foreach (var operation in operations)
                        {
                            var toolName = string.IsNullOrEmpty(options.LocalEndpoints.ToolPrefix)
                                ? operation.Name
                                : options.LocalEndpoints.ToolPrefix + operation.Name;

                            if (toolCollection.Any(t => t.ProtocolTool.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase)))
                            {
                                logger.LogDebug("[MCPify] Skipping duplicate local tool registration for {ToolName}.", toolName);
                                continue;
                            }

                            var descriptor = operation with { Name = toolName };

                            var localOpts = new McpifyOptions
                            {
                                DefaultHeaders = options.LocalEndpoints.DefaultHeaders
                            };

                            var hasSecurity = descriptor.Operation.Security != null && descriptor.Operation.Security.Count > 0;

                            var effectiveUpstreamAuth = hasSecurity ? options.LocalEndpoints.UpstreamAuth : UpstreamAuth.None();
                            var tokenProvider = TokenProviderFactory.Create(services, effectiveUpstreamAuth);
                            var tool = new OpenApiProxyTool(descriptor, BaseUrlProvider, httpClient, services.GetRequiredService<IJsonSchemaGenerator>(), localOpts, tokenProvider);
                            toolCollection.Add(tool);
                            count++;
                        }
                        logger.LogInformation("[MCPify] Successfully registered {Count} local endpoint tools.", count);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[MCPify] Error registering local endpoints in MapMcpifyEndpoint.");
            }
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
