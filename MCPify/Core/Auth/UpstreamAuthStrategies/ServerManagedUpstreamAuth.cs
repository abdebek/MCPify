using MCPify.Core.Auth.TokenProviders;

namespace MCPify.Core.Auth.UpstreamAuthStrategies;

/// <summary>
/// MCPify manages authentication via a server-side <see cref="IAuthenticationProvider"/>.
/// </summary>
internal sealed class ServerManagedUpstreamAuth : UpstreamAuth
{
    private readonly Func<IServiceProvider, IAuthenticationProvider> _factory;

    internal ServerManagedUpstreamAuth(Func<IServiceProvider, IAuthenticationProvider> factory)
    {
        _factory = factory;
    }

    internal override ITokenProvider Build(IServiceProvider serviceProvider)
    {
        return new AuthenticationFactoryTokenProvider(_factory, serviceProvider);
    }
}
