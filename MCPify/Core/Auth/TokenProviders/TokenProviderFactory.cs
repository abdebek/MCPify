namespace MCPify.Core.Auth.TokenProviders;

/// <summary>
/// Factory for creating ITokenProvider instances based on TokenSource configuration.
/// </summary>
public static class TokenProviderFactory
{
    /// <summary>
    /// Creates an <see cref="ITokenProvider"/> preferring <paramref name="upstreamAuth"/> when set,
    /// falling back to the legacy <paramref name="tokenSource"/> path.
    /// </summary>
    public static ITokenProvider Create(
        IServiceProvider serviceProvider,
        UpstreamAuth? upstreamAuth,
        TokenSource tokenSource,
        Func<IServiceProvider, IAuthenticationProvider>? authenticationFactory = null)
    {
        if (upstreamAuth != null)
            return upstreamAuth.Build(serviceProvider);

        return Create(serviceProvider, tokenSource, authenticationFactory);
    }

    /// <summary>
    /// Creates an ITokenProvider based on the specified TokenSource and authentication factory.
    /// </summary>
    /// <param name="serviceProvider">Service provider for resolving dependencies</param>
    /// <param name="tokenSource">The token source configuration</param>
    /// <param name="authenticationFactory">Optional authentication factory for server-managed auth</param>
    /// <returns>An ITokenProvider instance</returns>
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
