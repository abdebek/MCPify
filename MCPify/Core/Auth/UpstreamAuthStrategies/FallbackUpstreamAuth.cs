using MCPify.Core.Auth.TokenProviders;

namespace MCPify.Core.Auth.UpstreamAuthStrategies;

/// <summary>
/// Tries each strategy in order until one returns a token.
/// </summary>
internal sealed class FallbackUpstreamAuth : UpstreamAuth
{
    private readonly UpstreamAuth[] _strategies;

    internal FallbackUpstreamAuth(UpstreamAuth[] strategies)
    {
        _strategies = strategies;
    }

    internal override ITokenProvider Build(IServiceProvider serviceProvider)
    {
        var providers = _strategies
            .Select(s => s.Build(serviceProvider))
            .ToArray();
        return new CompositeTokenProvider(providers);
    }
}
