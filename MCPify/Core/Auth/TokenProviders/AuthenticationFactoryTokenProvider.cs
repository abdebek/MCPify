namespace MCPify.Core.Auth.TokenProviders;

/// <summary>
/// Token provider that wraps existing IAuthenticationProvider implementations.
/// Used for server-managed authentication (OAuth, API keys, etc.).
/// </summary>
public class AuthenticationFactoryTokenProvider : ITokenProvider
{
    private readonly Func<IServiceProvider, IAuthenticationProvider>? _authenticationFactory;
    private readonly IServiceProvider _serviceProvider;

    public AuthenticationFactoryTokenProvider(
        Func<IServiceProvider, IAuthenticationProvider>? authenticationFactory,
        IServiceProvider serviceProvider)
    {
        _authenticationFactory = authenticationFactory;
        _serviceProvider = serviceProvider;
    }

    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_authenticationFactory == null)
        {
            return null;
        }

        var authProvider = _authenticationFactory.Invoke(_serviceProvider);

        using var tempRequest = new HttpRequestMessage();
        await authProvider.ApplyAsync(tempRequest, cancellationToken);

        var authHeader = tempRequest.Headers.Authorization;
        if (authHeader != null)
        {
            return $"{authHeader.Scheme} {authHeader.Parameter}";
        }

        return null;
    }
}
