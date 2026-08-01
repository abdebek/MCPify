using MCPify.OpenApi;
using Microsoft.Extensions.DependencyInjection;

namespace MCPify.Core.Auth.OAuth;

/// <summary>
/// Options for building an <see cref="OAuthAuthorizationCodeAuthentication"/> instance
/// from explicit URLs or from an OpenAPI document's OAuth2 security scheme.
/// </summary>
public sealed class OAuthAuthorizationCodeAuthenticatorOptions
{
    /// <summary>OAuth client id (required).</summary>
    public required string ClientId { get; set; }

    /// <summary>OAuth client secret (optional for public clients).</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Redirect URI registered with the authorization server (required).</summary>
    public required string RedirectUri { get; set; }

    /// <summary>HMAC secret for OAuth state (≥ 32 characters), or use env <c>MCPIFY_STATE_SECRET</c>.</summary>
    public string? StateSecret { get; set; }

    /// <summary>Space-separated scopes. If null and OpenAPI/config provides scopes, those are used.</summary>
    public string? Scope { get; set; }

    /// <summary>Optional RFC 8707 resource indicator.</summary>
    public string? ResourceUrl { get; set; }

    /// <summary>Optional token-store provider name (defaults to auto-namespace from client id + token host).</summary>
    public string? ProviderName { get; set; }

    /// <summary>Enable PKCE (default true).</summary>
    public bool UsePkce { get; set; } = true;

    /// <summary>Dual-write tokens under default session (stdio only; default false).</summary>
    public bool AllowDefaultSessionFallback { get; set; }

    /// <summary>Authorization endpoint (required unless OpenAPI URL/path or <see cref="Configuration"/> is set).</summary>
    public string? AuthorizationEndpoint { get; set; }

    /// <summary>Token endpoint (required unless OpenAPI URL/path or <see cref="Configuration"/> is set).</summary>
    public string? TokenEndpoint { get; set; }

    /// <summary>Pre-parsed OAuth2 configuration (e.g. from <see cref="OpenApiOAuthParser"/>).</summary>
    public OAuth2Configuration? Configuration { get; set; }

    /// <summary>OpenAPI document URL used to discover authorization/token URLs and scopes.</summary>
    public string? OpenApiUrl { get; set; }

    /// <summary>Local OpenAPI file path used to discover authorization/token URLs and scopes.</summary>
    public string? OpenApiFilePath { get; set; }
}

/// <summary>
/// End-user-friendly factory for <see cref="OAuthAuthorizationCodeAuthentication"/>.
/// Loads Authorization Code endpoints/scopes from OpenAPI security schemes when requested.
/// </summary>
public static class OAuthAuthorizationCodeAuthenticator
{
    /// <summary>
    /// Creates an auth-code provider from options, optionally loading OpenAPI OAuth2 metadata first.
    /// </summary>
    public static async Task<OAuthAuthorizationCodeAuthentication> CreateAsync(
        OAuthAuthorizationCodeAuthenticatorOptions options,
        ISecureTokenStore secureTokenStore,
        IMcpContextAccessor mcpContextAccessor,
        HttpClient? httpClient = null,
        IOpenApiProvider? openApiProvider = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(secureTokenStore);
        ArgumentNullException.ThrowIfNull(mcpContextAccessor);
        _ = cancellationToken;

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            throw new ArgumentException("ClientId is required.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.RedirectUri))
        {
            throw new ArgumentException("RedirectUri is required.", nameof(options));
        }

        var config = options.Configuration;
        var openApiSource = options.OpenApiFilePath ?? options.OpenApiUrl;
        if (config is null && !string.IsNullOrWhiteSpace(openApiSource))
        {
            var provider = openApiProvider ?? new OpenApiV3Provider();
            var document = await provider.LoadAsync(openApiSource).ConfigureAwait(false);
            config = new OpenApiOAuthParser().Parse(document)
                ?? throw new InvalidOperationException(
                    $"No OAuth2 authorization_code security scheme found in OpenAPI source '{openApiSource}'.");
        }

        var authorizationEndpoint = options.AuthorizationEndpoint ?? config?.AuthorizationUrl;
        var tokenEndpoint = options.TokenEndpoint ?? config?.TokenUrl;
        if (string.IsNullOrWhiteSpace(authorizationEndpoint) || string.IsNullOrWhiteSpace(tokenEndpoint))
        {
            throw new InvalidOperationException(
                "AuthorizationEndpoint and TokenEndpoint are required. Provide them explicitly, via Configuration, or via OpenApiUrl/OpenApiFilePath.");
        }

        var scope = options.Scope;
        if (string.IsNullOrWhiteSpace(scope) && config?.Scopes is { Count: > 0 })
        {
            scope = string.Join(' ', config.Scopes.Keys);
        }

        scope ??= string.Empty;

        return new OAuthAuthorizationCodeAuthentication(
            clientId: options.ClientId,
            authorizationEndpoint: authorizationEndpoint,
            tokenEndpoint: tokenEndpoint,
            scope: scope,
            secureTokenStore: secureTokenStore,
            mcpContextAccessor: mcpContextAccessor,
            clientSecret: options.ClientSecret,
            httpClient: httpClient,
            redirectUri: options.RedirectUri,
            usePkce: options.UsePkce,
            stateSecret: options.StateSecret,
            allowDefaultSessionFallback: options.AllowDefaultSessionFallback,
            resourceUrl: options.ResourceUrl,
            providerName: options.ProviderName);
    }

    /// <summary>
    /// Registers a singleton <see cref="OAuthAuthorizationCodeAuthentication"/> built from options
    /// (optionally loading OpenAPI OAuth2 metadata). Also registers the discovered OAuth configuration
    /// into <see cref="OAuthConfigurationStore"/> when OpenAPI is used.
    /// </summary>
    public static IServiceCollection AddOAuthAuthorizationCodeAuthenticator(
        this IServiceCollection services,
        Action<OAuthAuthorizationCodeAuthenticatorOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddSingleton(sp =>
        {
            var options = new OAuthAuthorizationCodeAuthenticatorOptions
            {
                ClientId = "",
                RedirectUri = ""
            };
            configure(options);

            var store = sp.GetRequiredService<ISecureTokenStore>();
            var accessor = sp.GetRequiredService<IMcpContextAccessor>();
            var httpFactory = sp.GetService<IHttpClientFactory>();
            var httpClient = httpFactory?.CreateClient();
            var openApi = sp.GetService<IOpenApiProvider>();

            // Sync-over-async at startup is intentional for simple host registration.
            var auth = CreateAsync(options, store, accessor, httpClient, openApi)
                .GetAwaiter()
                .GetResult();

            // Publish PRM scopes when OpenAPI/config provided them
            if (options.Configuration is not null || !string.IsNullOrWhiteSpace(options.OpenApiUrl) || !string.IsNullOrWhiteSpace(options.OpenApiFilePath))
            {
                var oauthStore = sp.GetService<OAuthConfigurationStore>();
                if (oauthStore is not null)
                {
                    var cfg = options.Configuration;
                    if (cfg is null)
                    {
                        var source = options.OpenApiFilePath ?? options.OpenApiUrl!;
                        var provider = openApi ?? new OpenApiV3Provider();
                        var doc = provider.LoadAsync(source).GetAwaiter().GetResult();
                        cfg = new OpenApiOAuthParser().Parse(doc);
                    }

                    if (cfg is not null)
                    {
                        oauthStore.AddConfiguration(cfg);
                    }
                }
            }

            return auth;
        });

        return services;
    }
}
