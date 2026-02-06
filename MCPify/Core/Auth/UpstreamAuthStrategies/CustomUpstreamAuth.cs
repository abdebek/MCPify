namespace MCPify.Core.Auth.UpstreamAuthStrategies;

/// <summary>
/// Escape hatch — user-provided <see cref="ITokenProvider"/> factory.
/// </summary>
internal sealed class CustomUpstreamAuth : UpstreamAuth
{
    private readonly Func<IServiceProvider, ITokenProvider> _factory;

    internal CustomUpstreamAuth(Func<IServiceProvider, ITokenProvider> factory)
    {
        _factory = factory;
    }

    internal override ITokenProvider Build(IServiceProvider serviceProvider)
    {
        return _factory(serviceProvider);
    }
}
