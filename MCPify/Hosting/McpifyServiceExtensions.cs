using MCPify.Core;
using MCPify.OpenApi;
using MCPify.Schema;
using MCPify.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace MCPify.Hosting;

public static class McpifyServiceExtensions
{
    public static IServiceCollection AddMcpify(
        this IServiceCollection services,
        string swaggerUrl,
        string apiBaseUrl,
        Action<McpifyOptions>? configure = null)
    {
        var opts = new McpifyOptions();
        configure?.Invoke(opts);

        services.AddMcpServer()
            .WithHttpTransport();

        services.AddHttpClient();

        services.AddSingleton<IOpenApiProvider>(_ =>
            opts.ProviderOverride ?? new OpenApiV3Provider());

        services.AddSingleton<IJsonSchemaGenerator>(_ =>
            opts.SchemaGeneratorOverride ?? new DefaultJsonSchemaGenerator());

        // Load OpenAPI and register tools directly
        var tempProvider = new ServiceCollection()
            .AddSingleton<IOpenApiProvider>(opts.ProviderOverride ?? new OpenApiV3Provider())
            .BuildServiceProvider();

        var provider = tempProvider.GetRequiredService<IOpenApiProvider>();
        var document = provider.LoadAsync(swaggerUrl).GetAwaiter().GetResult();
        var operations = provider.GetOperations(document);

        if (opts.Filter != null)
        {
            operations = operations.Where(opts.Filter);
        }

        // Register each tool as a singleton McpServerTool
        foreach (var operation in operations)
        {
            var toolName = string.IsNullOrEmpty(opts.ToolPrefix)
                ? operation.Name
                : opts.ToolPrefix + operation.Name;

            var descriptor = operation with { Name = toolName };

            services.AddSingleton<McpServerTool>(sp =>
            {
                var httpClient = sp.GetRequiredService<HttpClient>();
                var schema = sp.GetRequiredService<IJsonSchemaGenerator>();
                return new OpenApiProxyTool(descriptor, apiBaseUrl, httpClient, schema);
            });
        }

        return services;
    }
}
