namespace MCPify.Core.Auth.TokenProviders;

/// <summary>
/// Factory for creating ITokenProvider instances.
/// </summary>
public static class TokenProviderFactory
{
    /// <summary>
    /// Creates an <see cref="ITokenProvider"/> from an explicit <see cref="UpstreamAuth"/> strategy.
    /// </summary>
    public static ITokenProvider Create(
        IServiceProvider serviceProvider,
        UpstreamAuth? upstreamAuth)
    {
        return upstreamAuth?.Build(serviceProvider) ?? NoTokenProvider.Instance;
    }

    /// <summary>
    /// Legacy factory for creating an <see cref="ITokenProvider"/> from <see cref="TokenSource"/>.
    /// New code should use <see cref="Create(IServiceProvider,UpstreamAuth?)"/>.
    /// </summary>
    /// <param name="serviceProvider">Service provider for resolving dependencies</param>
    /// <param name="tokenSource">The token source configuration</param>
    /// <param name="authenticationFactory">Optional authentication factory for server-managed auth</param>
    /// <returns>An ITokenProvider instance</returns>
    [Obsolete("Use Create(IServiceProvider, UpstreamAuth?) instead.")]
    public static ITokenProvider Create(
        IServiceProvider serviceProvider,
        TokenSource tokenSource,
        Func<IServiceProvider, IAuthenticationProvider>? authenticationFactory = null)
    {
        return tokenSource switch
        {
            TokenSource.Server => new AuthenticationFactoryTokenProvider(authenticationFactory, serviceProvider),

            TokenSource.Client => new McpContextTokenProvider(
                serviceProvider.GetRequiredService<IMcpContextAccessor>()),

            TokenSource.Both => new CompositeTokenProvider(new ITokenProvider[]
            {
                new McpContextTokenProvider(serviceProvider.GetRequiredService<IMcpContextAccessor>()),
                new AuthenticationFactoryTokenProvider(authenticationFactory, serviceProvider)
            }),

            TokenSource.None => NoTokenProvider.Instance,

            _ => throw new ArgumentOutOfRangeException(nameof(tokenSource), tokenSource, "Invalid TokenSource value")
        };
    }
}
