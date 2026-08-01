using MCPify.Core.Auth;
using MCPify.OpenApi;
using MCPify.Schema;
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
    /// Controls whether remote OpenAPI URLs are allowed and which addresses are blocked.
    /// Defaults to blocking loopback, link-local, and RFC1918 private ranges to prevent SSRF.
    /// Set <see cref="SsrfGuard.AllowPrivateAddresses"/> to true to allow internal addresses
    /// (e.g. for self-hosted APIs behind a proxy).
    /// </summary>
    public SsrfGuard SsrfGuard { get; set; } = new();

    /// <summary>
    /// The transport mechanism to use for the MCP server. Defaults to HTTP.
    /// </summary>
    public McpTransportType Transport { get; set; } = McpTransportType.Http;

    /// <summary>
    /// Allows forwarding MCP client bearer tokens to upstream APIs when HTTP transport is used.
    /// Disabled by default for hosted/multi-user safety.
    /// </summary>
    public bool AllowClientTokenPassthrough { get; set; }

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

    internal bool HttpPassThroughConfigured { get; set; }

    internal bool HttpPassThroughWarningLogged { get; set; }

}

/// <summary>
/// Defines where authentication tokens should be sourced from.
/// </summary>
public enum TokenSource
{
    /// <summary>
    /// MCPify server manages authentication (OAuth flows, API keys, etc.).
    /// This is the default for backward compatibility.
    /// </summary>
    Server,

    /// <summary>
    /// MCP client provides the authentication token directly.
    /// Use this when the client handles OAuth or other authentication flows.
    /// </summary>
    Client,

    /// <summary>
    /// Try client token first, then fall back to server authentication.
    /// Useful for hybrid scenarios.
    /// </summary>
    Both,

    /// <summary>
    /// No authentication required.
    /// </summary>
    None
}

/// <summary>
/// Defines the available transport types for the MCP server.
/// </summary>
public enum McpTransportType
{
    /// <summary>
    /// Uses Streamable HTTP for communication. Best for remote servers.
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
    /// Configures how MCPify acquires tokens for upstream API calls.
    /// When set, takes precedence over <see cref="TokenSource"/> and <see cref="AuthenticationFactory"/>.
    /// </summary>
    public UpstreamAuth? UpstreamAuth { get; set; }

    /// <summary>
    /// A factory for creating an authentication provider for local endpoints.
    /// </summary>
    [Obsolete("Use UpstreamAuth.ServerManaged() instead.")]
    public Func<IServiceProvider, IAuthenticationProvider>? AuthenticationFactory { get; set; }

    /// <summary>
    /// Specifies where authentication tokens should be sourced from.
    /// Legacy compatibility path used when <see cref="UpstreamAuth"/> is not configured.
    /// </summary>
    [Obsolete("Use UpstreamAuth instead.")]
    public TokenSource TokenSource { get; set; } = TokenSource.Server;
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
    /// Configures how MCPify acquires tokens for upstream API calls.
    /// When set, takes precedence over <see cref="TokenSource"/> and <see cref="AuthenticationFactory"/>.
    /// </summary>
    public UpstreamAuth? UpstreamAuth { get; set; }

    /// <summary>
    /// A factory for creating an authentication provider for this API.
    /// </summary>
    [Obsolete("Use UpstreamAuth.ServerManaged() instead.")]
    public Func<IServiceProvider, IAuthenticationProvider>? AuthenticationFactory { get; set; }

    /// <summary>
    /// Specifies where authentication tokens should be sourced from.
    /// Legacy compatibility path used when <see cref="UpstreamAuth"/> is not configured.
    /// </summary>
    [Obsolete("Use UpstreamAuth instead.")]
    public TokenSource TokenSource { get; set; } = TokenSource.Server;
}

/// <summary>
/// Controls SSRF protection for remote OpenAPI URL fetching.
/// </summary>
public class SsrfGuard
{
    /// <summary>
    /// When true, allows fetching from loopback, link-local, and RFC1918 private addresses.
    /// Default is false (block private addresses). Enable for self-hosted APIs behind a proxy
    /// where the OpenAPI URL points to an internal service.
    /// </summary>
    public bool AllowPrivateAddresses { get; set; }

    /// <summary>
    /// When true, disables all SSRF checks. Only use in fully trusted environments.
    /// </summary>
    public bool DisableSsrfChecks { get; set; }

    /// <summary>
    /// Additional hostnames or IP ranges to block (e.g. metadata endpoints).
    /// Entries are matched case-insensitively against the resolved host.
    /// Defaults to blocking cloud metadata endpoints.
    /// </summary>
    public HashSet<string> BlockedHosts { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "169.254.169.254", // AWS/GCP/Azure metadata
        "metadata.google.internal",
        "metadata.azure.com",
    };
}
