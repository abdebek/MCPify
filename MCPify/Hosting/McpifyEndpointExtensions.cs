using MCPify.Core;
using MCPify.Core.Auth;
using MCPify.Core.Auth.TokenProviders;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
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

                            #pragma warning disable CS0618 // Obsolete TokenSource/AuthenticationFactory - backward compat
                            var tokenProvider = TokenProviderFactory.Create(services, options.LocalEndpoints.UpstreamAuth, options.LocalEndpoints.TokenSource, options.LocalEndpoints.AuthenticationFactory);
                            #pragma warning restore CS0618
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
                mcpRoute.RequireAuthorization();
            }
        }

        // Map OAuth Protected Resource Metadata only if auth is configured
        if (oauthStore != null)
        {
            app.MapGet("/.well-known/oauth-protected-resource", (OAuthConfigurationStore store, IServer server, McpifyOptions opts) =>
            {
                var configs = store.GetConfigurations().ToList();
                if (!configs.Any())
                {
                    return Results.NotFound(new { error = "OAuth not configured" });
                }

                var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
                var resourceUrl = opts.ResourceUrlOverride;
                if (string.IsNullOrWhiteSpace(resourceUrl))
                {
                    resourceUrl = opts.LocalEndpoints?.BaseUrlOverride;
                }

                if (string.IsNullOrWhiteSpace(resourceUrl))
                {
                    resourceUrl = addresses?.FirstOrDefault();
                }

                resourceUrl = (string.IsNullOrWhiteSpace(resourceUrl) ? Constants.DefaultBaseUrl : resourceUrl).TrimEnd('/');

                static IEnumerable<string> ResolveAuthorizationServers(OAuth2Configuration config)
                {
                    if (Uri.TryCreate(config.AuthorizationUrl, UriKind.Absolute, out var uri))
                    {
                        yield return uri.GetLeftPart(UriPartial.Authority);
                    }
                }

                // If any config has AuthorizationServers, only use those; otherwise, fall back to AuthorizationUrl authority.
                List<string> issuers;
                if (configs.Any(c => c.AuthorizationServers?.Any() == true))
                {
                    issuers = configs
                        .Where(c => c.AuthorizationServers?.Any() == true)
                        .SelectMany(c => c.AuthorizationServers)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                else
                {
                    issuers = configs
                        .SelectMany(ResolveAuthorizationServers)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

                return Results.Ok(new
                {
                    resource = resourceUrl,
                    authorization_servers = issuers,
                    scopes_supported = configs.SelectMany(c => c.Scopes.Keys).Distinct().ToList(),
                    bearer_methods_supported = new[] { "header" } // We only support Bearer header
                });
            })
            .ExcludeFromDescription();
        }

        return app;
    }
}
