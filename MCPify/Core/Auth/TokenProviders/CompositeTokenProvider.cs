namespace MCPify.Core.Auth.TokenProviders;

/// <summary>
/// Token provider that tries multiple providers in order until one applies auth.
/// Useful for implementing fallback behavior (e.g., try client token first, then server).
/// </summary>
public class CompositeTokenProvider : ITokenProvider
{
    private readonly IEnumerable<ITokenProvider> _providers;

    public CompositeTokenProvider(IEnumerable<ITokenProvider> providers)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
    }

    public async Task<bool> ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        foreach (var provider in _providers)
        {
            if (await provider.ApplyAsync(request, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }
}
