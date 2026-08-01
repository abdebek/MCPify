using MCPify.Core;
using MCPify.Endpoints;
using MCPify.OpenApi;
using MCPify.Schema;
using MCPify.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using MCPify.Core.Auth;
using MCPify.Core.Session;
using System.IO;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using System.Text.Json;

namespace MCPify.Hosting;

public static class McpifyServiceExtensions
{
    /// <summary>
    /// Adds MCPify services to the service collection with custom configuration.
    /// This method registers both core services and authentication services.
    /// For more granular control, use <see cref="AddMcpifyCore"/> and <see cref="AddMcpifyAuthentication"/> separately.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="configure">A delegate to configure the <see cref="McpifyOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddMcpify(
        this IServiceCollection services,
        Action<McpifyOptions> configure)
    {
        services.AddMcpifyCore(configure);
        services.AddMcpifyAuthentication();
        return services;
    }

    /// <summary>
    /// Adds only the core MCPify services without authentication.
    /// Use this when your MCP client handles authentication via UpstreamAuth.PassThrough() or no authentication is needed via UpstreamAuth.None().
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="configure">A delegate to configure the <see cref="McpifyOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddMcpifyCore(
        this IServiceCollection services,
        Action<McpifyOptions> configure)
    {
        var opts = new McpifyOptions();
        configure(opts);
        UpstreamAuthTransportPolicy.NormalizeAndValidate(opts);

        services.AddSingleton(opts);
        services.AddHostedService<HttpPassThroughWarningHostedService>();

        services.AddSingleton<McpServerPrimitiveCollection<McpServerTool>>();
        services.AddSingleton<McpifyServiceRegistrar>();
        // Register Session Map for "Lazy Authentication"
        services.AddSingleton<ISessionMap, InMemorySessionMap>();

        var serverBuilder = services.AddMcpServer();
        serverBuilder.AddAuthorizationFilters();
        if (opts.Transport == McpTransportType.Stdio)
        {
            serverBuilder.WithStdioServerTransport();
        }
        else
        {
            serverBuilder.WithHttpTransport();
        }

        services.AddHttpContextAccessor();
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

        services.AddSingleton<IOpenApiProvider>(sp =>
            opts.ProviderOverride ?? new OpenApiV3Provider(opts.OpenApiDownloadTimeout, opts.SsrfGuard, sp.GetService<IHttpClientFactory>()));

        services.AddSingleton<IJsonSchemaGenerator>(_ =>
            opts.SchemaGeneratorOverride ?? new DefaultJsonSchemaGenerator());

        services.AddSingleton<IEndpointMetadataProvider, AspNetCoreEndpointMetadataProvider>();

        // Register IMcpContextAccessor as singleton - AsyncLocal handles per-request isolation
        // (same pattern as ASP.NET Core's IHttpContextAccessor)
        services.AddSingleton<IMcpContextAccessor, McpContextAccessor>();

        // Register OAuth discovery services (core - needed by McpifyServiceRegistrar for OpenAPI parsing)
        services.AddSingleton<OpenApiOAuthParser>();

        var oauthStore = new OAuthConfigurationStore();
        foreach (var config in opts.OAuthConfigurations)
        {
            oauthStore.AddConfiguration(config);
        }
        services.AddSingleton(oauthStore);

        return services;
    }

    /// <summary>
    /// Adds MCPify authentication services including OAuth support, token storage, and auth tools.
    /// Call this after <see cref="AddMcpifyCore"/> when you need server-side authentication.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddMcpifyAuthentication(this IServiceCollection services)
    {
        // Register ISecureTokenStore
        services.AddSingleton<ISecureTokenStore>(sp =>
        {
            // Try IWebHostEnvironment first (web hosts), fall back to AppContext.BaseDirectory
            // (console/generic hosts) so MCPify works without Microsoft.NET.Sdk.Web.
            var env = sp.GetService<IWebHostEnvironment>();
            var basePath = env != null
                ? Path.Combine(env.ContentRootPath, "AuthTokens")
                : Path.Combine(AppContext.BaseDirectory, "AuthTokens");
            if (!Directory.Exists(basePath))
            {
                Directory.CreateDirectory(basePath);
            }
            return new EncryptedFileTokenStore(basePath);
        });

        // Register Auth Tools
        services.AddSingleton<McpServerTool, LoginTool>();
        services.AddSingleton<McpServerTool, SessionManagementTool>();

        services.AddSingleton<IAuthorizationHandler, ScopeRequirementHandler>();

        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<McpAuthenticationOptions>, McpAuthenticationOptionsSetup>());
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureNamedOptions<McpAuthenticationOptions>, McpAuthenticationOptionsSetup>());

        // Register the MCP authentication scheme by name only. Do NOT mutate the host app's
        // DefaultScheme / DefaultAuthenticateScheme / DefaultChallengeScheme — a library must
        // not hijack the app's default auth. Host apps that want MCP as the default can set
        // options.DefaultScheme = McpAuthenticationDefaults.AuthenticationScheme themselves.
        //
        // The "Bearer" alias is registered because the SDK's McpAuthenticationHandler
        // internally forwards to a "Bearer" scheme for token validation. We only register it
        // if no "Bearer" scheme exists yet, so a host's AddJwtBearer("Bearer") wins.
        services.AddAuthentication()
            .AddScheme<McpAuthenticationOptions, McpAuthenticationHandler>(
                McpAuthenticationDefaults.AuthenticationScheme,
                _ => { });

        services.TryAddBearerMcpScheme();

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
    /// Registers the "Bearer" alias for the MCP authentication handler, but only if no
    /// "Bearer" scheme is already registered. This avoids <see cref="InvalidOperationException"/>
    /// when the host has already called <c>AddJwtBearer("Bearer")</c>.
    /// </summary>
    private static void TryAddBearerMcpScheme(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<AuthenticationOptions>, ConfigureBearerScheme>());
    }

    /// <summary>
    /// <summary>
    /// Registers a custom MCP tool with a delegate handler.
    /// The tool is wrapped in SessionAwareToolDecorator automatically.
    /// </summary>
    public static IServiceCollection AddMcpifyTool(
        this IServiceCollection services,
        string name,
        string description,
        Func<RequestContext<CallToolRequestParams>, CancellationToken, ValueTask<CallToolResult>> handler,
        object? inputSchema = null)
    {
        services.AddSingleton<McpServerTool>(sp => new DelegateTool(name, description, inputSchema, handler));
        return services;
    }

    /// <summary>
    /// Registers a custom MCP tool with a simple string-returning handler.
    /// </summary>
    public static IServiceCollection AddMcpifyTool(
        this IServiceCollection services,
        string name,
        string description,
        Func<JsonElement?, CancellationToken, ValueTask<string>> handler,
        object? inputSchema = null)
    {
        services.AddSingleton<McpServerTool>(sp => DelegateToolBuilder.Create(name, description, handler, inputSchema));
        return services;
    }

    /// <summary>
    /// Configures the "Bearer" scheme to use <see cref="McpAuthenticationHandler"/> only if
    /// no handler has been registered for "Bearer" yet. Runs late so host registrations win.
    /// </summary>
    private sealed class ConfigureBearerScheme : IConfigureOptions<AuthenticationOptions>
    {
        public void Configure(AuthenticationOptions options) => Configure(McpAuthenticationDefaults.AuthenticationScheme, options);

        public void Configure(string? name, AuthenticationOptions options)
        {
            if (options.SchemeMap.ContainsKey("Bearer"))
            {
                return;
            }

            options.AddScheme("Bearer", b =>
            {
                b.HandlerType = typeof(McpAuthenticationHandler);
            });
        }
    }
}
