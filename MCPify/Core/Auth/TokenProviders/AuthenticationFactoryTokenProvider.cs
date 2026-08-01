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

    public async Task<bool> ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        if (_authenticationFactory == null)
        {
            return false;
        }

        var authProvider = _authenticationFactory.Invoke(_serviceProvider);
        await authProvider.ApplyAsync(request, cancellationToken);
        return true;
    }
}
