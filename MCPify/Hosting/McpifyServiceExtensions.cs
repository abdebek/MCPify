using MCPify.Core;
using MCPify.Endpoints;
using MCPify.OpenApi;
using MCPify.Schema;
using MCPify.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using System.Net.Http;

namespace MCPify.Hosting;

public static class McpifyServiceExtensions
{
    public static IServiceCollection AddMcpify(
        this IServiceCollection services,
        Action<McpifyOptions> configure)
    {
        var opts = new McpifyOptions();
        configure(opts);

        services.AddSingleton(opts);

        services.AddSingleton<McpServerPrimitiveCollection<McpServerTool>>();

        services.AddMcpServer()
            .WithHttpTransport();

        services.AddHttpClient();

        services.AddSingleton<IOpenApiProvider>(_ =>
            opts.ProviderOverride ?? new OpenApiV3Provider());

        services.AddSingleton<IJsonSchemaGenerator>(_ =>
            opts.SchemaGeneratorOverride ?? new DefaultJsonSchemaGenerator());

        services.AddSingleton<IEndpointMetadataProvider, AspNetCoreEndpointMetadataProvider>();

        // Register external API tools
        if (opts.ExternalApis.Count > 0)
        {
            var provider = opts.ProviderOverride ?? new OpenApiV3Provider();
            var schema = opts.SchemaGeneratorOverride ?? new DefaultJsonSchemaGenerator();

            foreach (var externalApi in opts.ExternalApis)
            {
                RegisterExternalApiTools(services, externalApi, opts, schema, provider);
            }
        }

        if (opts.LocalEndpoints?.Enabled == true)
        {
            services.AddSingleton(new LocalEndpointToolRegistration(opts.LocalEndpoints));
            services.AddHostedService<McpifyInitializer>();
        }

        return services;
    }

    public static IServiceCollection AddMcpify(
        this IServiceCollection services,
        string swaggerUrl,
        string apiBaseUrl,
        Action<McpifyOptions>? configure = null)
    {
        return services.AddMcpify(options =>
        {
            configure?.Invoke(options);

            options.ExternalApis.Add(new ExternalApiOptions
            {
                SwaggerUrl = swaggerUrl,
                ApiBaseUrl = apiBaseUrl,
                ToolPrefix = options.ExternalApis.Count == 0 ? null : $"api{options.ExternalApis.Count}_",
            });
        });
    }

    public static IServiceCollection AddMcpifyTestTool(this IServiceCollection services)
    {
        services.AddSingleton<McpServerTool, SimpleMathTool>();
        return services;
    }

    private static void RegisterExternalApiTools(
        IServiceCollection services,
        ExternalApiOptions apiOptions,
        McpifyOptions globalOptions,
        IJsonSchemaGenerator schema,
        IOpenApiProvider provider)
    {
        var source = apiOptions.SwaggerFilePath ?? apiOptions.SwaggerUrl;
        if (string.IsNullOrEmpty(source))
        {
            Console.WriteLine("[MCPify] WARNING: ExternalApiOptions requires either SwaggerUrl or SwaggerFilePath");
            return;
        }

        try
        {
            var document = provider.LoadAsync(source).GetAwaiter().GetResult();
            var operations = provider.GetOperations(document);

            if (apiOptions.Filter != null)
            {
                operations = operations.Where(apiOptions.Filter);
            }

            var httpClient = new HttpClient(); // Create a new instance during registration

            foreach (var operation in operations)
            {
                var toolName = string.IsNullOrEmpty(apiOptions.ToolPrefix)
                    ? operation.Name
                    : apiOptions.ToolPrefix + operation.Name;

                var descriptor = operation with { Name = toolName };

                var apiOpts = new McpifyOptions
                {
                    DefaultHeaders = new Dictionary<string, string>(globalOptions.DefaultHeaders)
                };

                foreach (var header in apiOptions.DefaultHeaders)
                {
                    apiOpts.DefaultHeaders[header.Key] = header.Value;
                }

                var tool = new OpenApiProxyTool(descriptor, apiOptions.ApiBaseUrl, httpClient, schema, apiOpts);

                // Register as singleton service - this is the correct pattern
                services.AddSingleton<McpServerTool>(tool);
            }

            Console.WriteLine($"[MCPify] Successfully registered {operations.Count()} tools from {source}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MCPify] WARNING: Failed to load OpenAPI spec from {source}.");
            Console.WriteLine($"[MCPify] Error: {ex.Message}");
        }
    }
}