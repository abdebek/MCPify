using MCPify.Core;
using MCPify.Endpoints;
using MCPify.Schema;
using MCPify.Tools;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;

namespace MCPify.Hosting;

internal class McpifyInitializer : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly McpifyOptions _options;
    private readonly IJsonSchemaGenerator _schema;
    private readonly IHttpClientFactory _httpClientFactory;

    public McpifyInitializer(
        IServiceProvider serviceProvider,
        McpifyOptions options,
        IJsonSchemaGenerator schema,
        IHttpClientFactory httpClientFactory)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _schema = schema;
        _httpClientFactory = httpClientFactory;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.LocalEndpoints?.Enabled == true)
        {
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1000, cancellationToken);

                    var attempts = 0;
                    while (attempts < 50 && !cancellationToken.IsCancellationRequested)
                    {
                        var endpointSources = _serviceProvider.GetServices<EndpointDataSource>();
                        var totalEndpoints = endpointSources.Sum(s => s.Endpoints.Count);

                        if (totalEndpoints > 0)
                        {
                            RegisterLocalEndpoints();
                            break;
                        }

                        attempts++;
                        await Task.Delay(200, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[MCPify] Error initializing local endpoints: {ex.Message}");
                }
            }, cancellationToken);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private void RegisterLocalEndpoints()
    {
        var endpointProvider = _serviceProvider.GetRequiredService<IEndpointMetadataProvider>() as AspNetCoreEndpointMetadataProvider;
        if (endpointProvider == null) return;

        var toolCollection = _serviceProvider.GetService<McpServerPrimitiveCollection<McpServerTool>>();
        if (toolCollection == null)
        {
             Console.WriteLine("[MCPify] Warning: McpServerPrimitiveCollection not found. Local endpoints cannot be registered.");
             return;
        }

        var operations = endpointProvider.GetLocalEndpoints();

        if (_options.LocalEndpoints!.Filter != null)
        {
            operations = operations.Where(_options.LocalEndpoints.Filter);
        }

        var httpClient = _httpClientFactory.CreateClient();

        // Dynamic Base URL Provider
        string BaseUrlProvider()
        {
            var server = _serviceProvider.GetService<IServer>();
            var addresses = server?.Features.Get<IServerAddressesFeature>()?.Addresses;
            return addresses?.FirstOrDefault() ?? "http://localhost:5000";
        }

        var count = 0;
        foreach (var operation in operations)
        {
            var toolName = string.IsNullOrEmpty(_options.LocalEndpoints.ToolPrefix)
                ? operation.Name
                : _options.LocalEndpoints.ToolPrefix + operation.Name;

            var descriptor = operation with { Name = toolName };

            var localOpts = new McpifyOptions
            {
                DefaultHeaders = _options.LocalEndpoints.DefaultHeaders
            };

            var tool = new OpenApiProxyTool(descriptor, BaseUrlProvider, httpClient, _schema, localOpts);
            toolCollection.Add(tool);
            count++;
        }

        Console.WriteLine($"[MCPify] Successfully registered {count} local endpoint tools.");
    }
}
