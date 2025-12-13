using MCPify.Core;
using MCPify.Endpoints;
using MCPify.OpenApi;
using MCPify.Schema;
using MCPify.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using MCPify.Core.Auth;
using System.IO;

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
        services.AddSingleton<McpifyServiceRegistrar>();

        var serverBuilder = services.AddMcpServer();
        if (opts.Transport == McpTransportType.Stdio)
        {
            serverBuilder.WithStdioServerTransport();
        }
        else
        {
            serverBuilder.WithHttpTransport();
        }

        services.AddHttpClient();

        services.AddOptions<McpServerOptions>()
            .PostConfigure<McpServerPrimitiveCollection<McpServerTool>>((options, sharedCollection) =>
            {
                if (options.ToolCollection != null && !ReferenceEquals(options.ToolCollection, sharedCollection))
                {
                    foreach (var tool in options.ToolCollection)
                    {
                        sharedCollection.Add(tool);
                    }
                }

                options.ToolCollection = sharedCollection;
            });

        services.AddSingleton<IOpenApiProvider>(_ =>
            opts.ProviderOverride ?? new OpenApiV3Provider(opts.OpenApiDownloadTimeout));

        services.AddSingleton<IJsonSchemaGenerator>(_ =>
            opts.SchemaGeneratorOverride ?? new DefaultJsonSchemaGenerator());

        services.AddSingleton<IEndpointMetadataProvider, AspNetCoreEndpointMetadataProvider>();

        // Register IMcpContextAccessor and its concrete implementation
        services.AddScoped<IMcpContextAccessor, McpContextAccessor>();

        // Register ISecureTokenStore
        services.AddSingleton<ISecureTokenStore>(sp =>
        {
            var env = sp.GetRequiredService<IWebHostEnvironment>();
            var basePath = Path.Combine(env.ContentRootPath, "AuthTokens");
            // Ensure the directory exists
            if (!Directory.Exists(basePath))
            {
                Directory.CreateDirectory(basePath);
            }
            return new EncryptedFileTokenStore(basePath);
        });

        services.AddSingleton<OpenApiOAuthParser>();
        services.AddSingleton<OAuthConfigurationStore>();

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

    // New extension method for adding McpContextMiddleware
    public static IApplicationBuilder UseMcpifyContext(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<McpContextMiddleware>();
    }

    public static IApplicationBuilder UseMcpifyOAuth(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<McpOAuthAuthenticationMiddleware>();
    }
}
