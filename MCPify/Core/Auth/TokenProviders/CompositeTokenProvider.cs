namespace MCPify.Core.Auth.TokenProviders;

/// <summary>
/// Token provider that tries multiple providers in order until one returns a token.
/// Useful for implementing fallback behavior (e.g., try client token first, then server).
/// </summary>
public class CompositeTokenProvider : ITokenProvider
{
    private readonly IEnumerable<ITokenProvider> _providers;

    public CompositeTokenProvider(IEnumerable<ITokenProvider> providers)
    {
        _providers = providers ?? throw new ArgumentNullException(nameof(providers));
    }

    public async Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        foreach (var provider in _providers)
        {
            var token = await provider.GetTokenAsync(cancellationToken);
            if (!string.IsNullOrEmpty(token))
            {
                return token;
            }
        }

        return null;
    }
}
