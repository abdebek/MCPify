using MCPify.Core;
using MCPify.OpenApi;
using MCPify.Schema;
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

        services.AddSingleton<IMcpifyInitializer>(sp =>
            new McpifyInitializer(sp, swaggerUrl, apiBaseUrl, opts));

        return services;
    }
}
