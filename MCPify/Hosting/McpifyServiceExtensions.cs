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
using MCPify.Core.Session;
using System.IO;

namespace MCPify.Hosting;

public static class McpifyServiceExtensions
{
    /// <summary>
    /// Adds MCPify services to the service collection with custom configuration.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="configure">A delegate to configure the <see cref="McpifyOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddMcpify(
        this IServiceCollection services,
        Action<McpifyOptions> configure)
    {
        var opts = new McpifyOptions();
        configure(opts);

        services.AddSingleton(opts);

        services.AddSingleton<McpServerPrimitiveCollection<McpServerTool>>();
        services.AddSingleton<McpifyServiceRegistrar>();
        
        // Register Session Map for "Lazy Authentication"
        services.AddSingleton<ISessionMap, InMemorySessionMap>();
        
        // Register Core Tools
        services.AddSingleton<McpServerTool, LoginTool>();
        services.AddSingleton<McpServerTool, SessionManagementTool>();

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
                        if (!sharedCollection.Any(t => t.ProtocolTool.Name.Equals(tool.ProtocolTool.Name, StringComparison.OrdinalIgnoreCase)))
                        {
                            sharedCollection.Add(tool);
                        }
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

        var oauthStore = new OAuthConfigurationStore();
        foreach (var config in opts.OAuthConfigurations)
        {
            oauthStore.AddConfiguration(config);
        }
        services.AddSingleton(oauthStore);

        // Register token validation services if enabled
        if (opts.TokenValidation != null)
        {
            services.AddSingleton(opts.TokenValidation);

            if (opts.TokenValidation.EnableJwtValidation)
            {
                services.AddSingleton<IAccessTokenValidator>(sp =>
                    new JwtAccessTokenValidator(sp.GetRequiredService<TokenValidationOptions>()));
            }

            // Register scope requirement store with access to OAuth configurations
            services.AddSingleton(sp =>
                new ScopeRequirementStore(
                    opts.ScopeRequirements,
                    sp.GetRequiredService<TokenValidationOptions>(),
                    sp.GetService<OAuthConfigurationStore>()));
        }
        else
        {
            // Register empty token validation options for when validation is not configured
            var defaultOptions = new TokenValidationOptions();
            services.AddSingleton(defaultOptions);
            services.AddSingleton(sp =>
                new ScopeRequirementStore(
                    opts.ScopeRequirements,
                    defaultOptions,
                    sp.GetService<OAuthConfigurationStore>()));
        }

        return services;
    }

    /// <summary>
    /// Adds MCPify services with simplified configuration for a single external API.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="openApiUrl">The URL of the Swagger/OpenAPI specification.</param>
    /// <param name="apiBaseUrl">The base URL of the API.</param>
    /// <param name="configure">Optional delegate to further configure <see cref="McpifyOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddMcpify(
        this IServiceCollection services,
        string openApiUrl,
        string apiBaseUrl,
        Action<McpifyOptions>? configure = null)
    {
        return services.AddMcpify(options =>
        {
            configure?.Invoke(options);

            options.ExternalApis.Add(new ExternalApiOptions
            {
                OpenApiUrl = openApiUrl,
                ApiBaseUrl = apiBaseUrl,
                ToolPrefix = options.ExternalApis.Count == 0 ? null : $"api{options.ExternalApis.Count}_",
            });
        });
    }

    /// <summary>
    /// Adds a simple math tool for testing purposes.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddMcpifyTestTool(this IServiceCollection services)
    {
        services.AddSingleton<McpServerTool, SimpleMathTool>();
        return services;
    }

    /// <summary>
    /// Adds the MCP context middleware to the pipeline. This is required for accessing session and connection information.
    /// </summary>
    /// <param name="builder">The <see cref="IApplicationBuilder"/> instance.</param>
    /// <returns>The <see cref="IApplicationBuilder"/> instance.</returns>
    public static IApplicationBuilder UseMcpifyContext(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<McpContextMiddleware>();
    }

    /// <summary>
    /// Adds the MCP OAuth authentication middleware to the pipeline. This handles token validation and challenges for protected endpoints.
    /// </summary>
    /// <param name="builder">The <see cref="IApplicationBuilder"/> instance.</param>
    /// <returns>The <see cref="IApplicationBuilder"/> instance.</returns>
    public static IApplicationBuilder UseMcpifyOAuth(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<McpOAuthAuthenticationMiddleware>();
    }
}
