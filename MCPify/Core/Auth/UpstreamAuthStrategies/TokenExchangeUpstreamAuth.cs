using MCPify.Core.Auth.TokenExchange;
using MCPify.Core.Auth.TokenProviders;

namespace MCPify.Core.Auth.UpstreamAuthStrategies;

/// <summary>
/// Exchanges the MCP client's access token for an upstream API token via RFC 8693.
/// Wraps <see cref="TokenExchangeAuthentication"/> in an <see cref="AuthenticationFactoryTokenProvider"/>.
/// Requires <c>AddMcpifyAuthentication()</c> for <see cref="ISecureTokenStore"/>.
/// </summary>
internal sealed class TokenExchangeUpstreamAuth : UpstreamAuth
{
    private readonly Action<TokenExchangeOptions> _configure;

    internal TokenExchangeUpstreamAuth(Action<TokenExchangeOptions> configure)
    {
        _configure = configure;
    }

    internal override ITokenProvider Build(IServiceProvider serviceProvider)
    {
        var options = new TokenExchangeOptions();
        _configure(options);

        if (string.IsNullOrEmpty(options.TokenEndpoint))
            throw new InvalidOperationException("TokenExchangeOptions.TokenEndpoint is required.");
        if (string.IsNullOrEmpty(options.ClientId))
            throw new InvalidOperationException("TokenExchangeOptions.ClientId is required.");

        Func<IServiceProvider, IAuthenticationProvider> factory = sp =>
            new TokenExchangeAuthentication(
                tokenEndpoint: options.TokenEndpoint,
                clientId: options.ClientId,
                clientSecret: options.ClientSecret,
                scope: options.Scope,
                resource: options.Resource,
                audience: options.Audience,
                tokenStore: sp.GetRequiredService<ISecureTokenStore>(),
                mcpContextAccessor: sp.GetRequiredService<IMcpContextAccessor>(),
                httpClient: options.HttpClient,
                providerName: options.ProviderName);

        return new AuthenticationFactoryTokenProvider(factory, serviceProvider);
    }
}
