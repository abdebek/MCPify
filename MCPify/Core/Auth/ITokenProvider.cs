namespace MCPify.Core.Auth;

/// <summary>
/// Abstraction for retrieving authentication tokens.
/// Separates token acquisition from token application.
/// </summary>
public interface ITokenProvider
{
    /// <summary>
    /// Retrieves an authentication token if available.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The token string, or null if no token is available</returns>
    Task<string?> GetTokenAsync(CancellationToken cancellationToken = default);
}
