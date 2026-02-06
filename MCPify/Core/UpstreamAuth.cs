using MCPify.Core.Auth;
using MCPify.Core.Auth.UpstreamAuthStrategies;

namespace MCPify.Core;

/// <summary>
/// Defines how MCPify acquires tokens for upstream API calls.
/// Use the static factory methods to create instances.
/// </summary>
public abstract class UpstreamAuth
{
    /// <summary>
    /// Builds the <see cref="ITokenProvider"/> for this strategy.
    /// </summary>
    internal abstract ITokenProvider Build(IServiceProvider serviceProvider);

    /// <summary>
    /// Forwards the MCP client's access token to the upstream API as-is.
    /// Equivalent to the legacy <c>TokenSource.Client</c>.
    /// </summary>
    public static UpstreamAuth PassThrough() => new PassThroughUpstreamAuth();

    /// <summary>
    /// No authentication — upstream API is public.
    /// Equivalent to the legacy <c>TokenSource.None</c>.
    /// </summary>
    public static UpstreamAuth None() => new NoneUpstreamAuth();

    /// <summary>
    /// MCPify manages authentication via a server-side <see cref="IAuthenticationProvider"/>.
    /// Equivalent to the legacy <c>TokenSource.Server</c> + <c>AuthenticationFactory</c>.
    /// </summary>
    /// <param name="factory">
    /// A factory that creates the <see cref="IAuthenticationProvider"/> from the DI container.
    /// </param>
    public static UpstreamAuth ServerManaged(Func<IServiceProvider, IAuthenticationProvider> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return new ServerManagedUpstreamAuth(factory);
    }

    /// <summary>
    /// Exchanges the MCP client's access token for an upstream API token using RFC 8693 Token Exchange.
    /// Requires <c>AddMcpifyAuthentication()</c> for <see cref="ISecureTokenStore"/>.
    /// </summary>
    /// <param name="configure">Configures the token exchange options.</param>
    public static UpstreamAuth TokenExchange(Action<TokenExchangeOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        return new TokenExchangeUpstreamAuth(configure);
    }

    /// <summary>
    /// Tries each strategy in order until one returns a token.
    /// Equivalent to the legacy <c>TokenSource.Both</c>.
    /// </summary>
    /// <param name="strategies">Ordered list of strategies to try.</param>
    public static UpstreamAuth Fallback(params UpstreamAuth[] strategies)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        if (strategies.Length < 2)
            throw new ArgumentException("Fallback requires at least two strategies.", nameof(strategies));
        return new FallbackUpstreamAuth(strategies);
    }

    /// <summary>
    /// Escape hatch — supply your own <see cref="ITokenProvider"/> factory.
    /// </summary>
    /// <param name="factory">
    /// A factory that creates a custom <see cref="ITokenProvider"/> from the DI container.
    /// </param>
    public static UpstreamAuth Custom(Func<IServiceProvider, ITokenProvider> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return new CustomUpstreamAuth(factory);
    }
}
