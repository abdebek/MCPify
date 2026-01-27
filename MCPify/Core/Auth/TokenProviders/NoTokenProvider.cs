namespace MCPify.Core.Auth.TokenProviders;

/// <summary>
/// Token provider that provides no authentication token.
/// Used when no authentication is required (e.g., public APIs).
/// </summary>
public sealed class NoTokenProvider : ITokenProvider
{
    /// <summary>
    /// Singleton instance of the no-token provider.
    /// </summary>
    public static readonly NoTokenProvider Instance = new();

    private NoTokenProvider()
    {
    }

    public Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }
}
