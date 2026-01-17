using MCPify.OpenApi;
using MCPify.Schema;
using MCPify.Core.Auth;
using Microsoft.AspNetCore.Http;

namespace MCPify.Core;

/// <summary>
/// Configuration options for the MCPify service.
/// </summary>
public class McpifyOptions
{
    /// <summary>
    /// Custom delegate to resolve the Session ID from the current HttpContext.
    /// If not provided, or returns null, defaults to HttpContext.Items["McpSessionId"] or Constants.DefaultSessionId.
    /// </summary>
    public Func<HttpContext, string?>? SessionIdResolver { get; set; }

    /// <summary>
    /// Configuration for exposing local ASP.NET Core endpoints as MCP tools.
    /// </summary>
    public LocalEndpointsOptions? LocalEndpoints { get; set; }

    /// <summary>
    /// Explicit URL advertised to MCP clients for OAuth resource metadata and challenges.
    /// Allows publishing a proxy-facing URL that differs from the server's listen address.
    /// </summary>
    public string? ResourceUrlOverride { get; set; }

    /// <summary>
    /// Configuration for importing external APIs via OpenAPI/Swagger as MCP tools.
    /// </summary>
    public List<ExternalApiOptions> ExternalApis { get; set; } = new();

    /// <summary>
    /// Optional override for the OpenAPI provider (e.g., for testing or custom loading logic).
    /// </summary>
    public IOpenApiProvider? ProviderOverride { get; set; }

    /// <summary>
    /// Optional override for the JSON schema generator.
    /// </summary>
    public IJsonSchemaGenerator? SchemaGeneratorOverride { get; set; }

    /// <summary>
    /// Global default headers to apply to all requests made by MCP tools.
    /// </summary>
    public Dictionary<string, string> DefaultHeaders { get; set; } = new();

    /// <summary>
    /// Timeout for downloading OpenAPI specifications from URLs. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan OpenApiDownloadTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The transport mechanism to use for the MCP server. Defaults to HTTP.
    /// </summary>
    public McpTransportType Transport { get; set; } = McpTransportType.Http;

    /// <summary>
    /// Controls how the login tool handles browser launching for OAuth authentication.
    /// Set to <see cref="BrowserLaunchBehavior.Never"/> for headless/remote environments to avoid
    /// unnecessary timeouts waiting for browser launch to fail.
    /// Defaults to <see cref="BrowserLaunchBehavior.Auto"/> which detects headless environments at runtime.
    /// </summary>
    public BrowserLaunchBehavior LoginBrowserBehavior { get; set; } = BrowserLaunchBehavior.Auto;
    
    /// <summary>
    /// Optional list of OAuth2 configurations to be added to the OAuthConfigurationStore.
    /// </summary>
    public List<OAuth2Configuration> OAuthConfigurations { get; set; } = new();

    /// <summary>
    /// Configuration for JWT token validation behavior.
    /// When null or EnableJwtValidation is false, token validation is skipped for backward compatibility.
    /// </summary>
    public TokenValidationOptions? TokenValidation { get; set; }

    /// <summary>
    /// Per-tool scope requirements. Patterns support wildcards (* and ?).
    /// These are checked when <see cref="TokenValidation.ValidateScopes"/> is true.
    /// </summary>
    public List<ScopeRequirement> ScopeRequirements { get; set; } = new();
}

/// <summary>
/// Defines the available transport types for the MCP server.
/// </summary>
public enum McpTransportType
{
    /// <summary>
    /// Uses Server-Sent Events (SSE) and HTTP POST for communication. Best for remote servers.
    /// </summary>
    Http,
    /// <summary>
    /// Uses Standard Input/Output (Stdio) for communication. Best for local integration with desktop apps (e.g. Claude).
    /// </summary>
    Stdio
}

/// <summary>
/// Defines how the login tool should handle browser launching for OAuth flows.
/// </summary>
public enum BrowserLaunchBehavior
{
    /// <summary>
    /// Automatically detect if browser launching is possible.
    /// Skips browser launch in headless environments (no DISPLAY/WAYLAND on Linux, containers, SSH without X forwarding).
    /// </summary>
    Auto,
    /// <summary>
    /// Always attempt to open the browser, regardless of environment detection.
    /// </summary>
    Always,
    /// <summary>
    /// Never attempt to open the browser. Always return the URL directly for manual authentication.
    /// This is ideal for headless servers, containers, and remote environments.
    /// </summary>
    Never
}

/// <summary>
/// Options for configuring how local endpoints are exposed as MCP tools.
/// </summary>
public class LocalEndpointsOptions
{
    /// <summary>
    /// Whether to enable scanning and registration of local endpoints.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// An optional prefix to prepend to the names of generated tools.
    /// </summary>
    public string? ToolPrefix { get; set; }

    /// <summary>
    /// Overrides the base URL used when invoking local endpoints. Useful if the app listens on multiple addresses or runs behind a proxy.
    /// </summary>
    public string? BaseUrlOverride { get; set; }

    /// <summary>
    /// A filter predicate to include/exclude specific operations based on their descriptor.
    /// </summary>
    public Func<OpenApiOperationDescriptor, bool>? Filter { get; set; }

    /// <summary>
    /// Default headers to apply to requests to local endpoints.
    /// </summary>
    public Dictionary<string, string> DefaultHeaders { get; set; } = new();

    /// <summary>
    /// A factory for creating an authentication provider for local endpoints.
    /// </summary>
    public Func<IServiceProvider, IAuthenticationProvider>? AuthenticationFactory { get; set; }
}

/// <summary>
/// Options for configuring an external API to be proxied as MCP tools.
/// </summary>
public class ExternalApiOptions
{
    /// <summary>
    /// The URL of the OpenAPI/Swagger JSON specification.
    /// </summary>
    public string? OpenApiUrl { get; set; }

    /// <summary>
    /// The local file path to the OpenAPI/Swagger JSON specification.
    /// </summary>
    public string? OpenApiFilePath { get; set; }

    /// <summary>
    /// The base URL of the API to invoke.
    /// </summary>
    public required string ApiBaseUrl { get; set; }

    /// <summary>
    /// An optional prefix to prepend to the names of generated tools for this API.
    /// </summary>
    public string? ToolPrefix { get; set; }

    /// <summary>
    /// A filter predicate to include/exclude specific operations based on their descriptor.
    /// </summary>
    public Func<OpenApiOperationDescriptor, bool>? Filter { get; set; }

    /// <summary>
    /// Default headers to apply to requests to this API.
    /// </summary>
    public Dictionary<string, string> DefaultHeaders { get; set; } = new();

    /// <summary>
    /// A factory for creating an authentication provider for this API.
    /// </summary>
    public Func<IServiceProvider, IAuthenticationProvider>? AuthenticationFactory { get; set; }
}
