using MCPify.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using MCPify.Endpoints;
using MCPify.Tools;
using MCPify.Schema;
using System.Net.Http;

namespace MCPify.Hosting;

public static class McpifyEndpointExtensions
{
    public static WebApplication MapMcpifyEndpoint(
        this WebApplication app,
        string path = "")
    {
        var options = app.Services.GetService<McpifyOptions>();
        var logger = app.Services.GetRequiredService<ILogger<WebApplication>>();

        if (options == null)
        {
            logger.LogError("[MCPify] McpifyOptions not found. Cannot map MCP endpoints.");
            return app;
        }

        if (options.LocalEndpoints?.Enabled == true)
        {
            try
            {
                var endpointProvider = app.Services.GetRequiredService<IEndpointMetadataProvider>() as AspNetCoreEndpointMetadataProvider;
                if (endpointProvider == null)
                {
                    logger.LogError("[MCPify] AspNetCoreEndpointMetadataProvider not found for local endpoints.");
                }
                else
                {
                    var toolCollection = app.Services.GetService<McpServerPrimitiveCollection<McpServerTool>>();
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

                        var httpClient = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient();

                        string BaseUrlProvider()
                        {
                            var server = app.Services.GetService<IServer>();
                            var addresses = server?.Features.Get<IServerAddressesFeature>()?.Addresses;
                            var baseUrl = addresses?.FirstOrDefault() ?? Constants.DefaultBaseUrl;
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

                            var tool = new OpenApiProxyTool(descriptor, BaseUrlProvider, httpClient, app.Services.GetRequiredService<IJsonSchemaGenerator>(), localOpts, options.LocalEndpoints.Authentication);
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

        if (options.Transport == McpTransportType.Http)
        {
            app.MapMcp(path);
        }

        return app;
    }
}
