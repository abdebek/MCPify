namespace MCPify.Core.Auth;

/// <summary>
/// Abstraction for applying authentication to an upstream HTTP request.
/// Separates token acquisition from token application and supports arbitrary
/// request mutations (Authorization header, API key headers, query params, etc.).
/// </summary>
public interface ITokenProvider
{
    /// <summary>
    /// Applies authentication to the <paramref name="request"/> if a token is available.
    /// </summary>
    /// <param name="request">The request to authenticate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if authentication was applied; <c>false</c> if no token was available.</returns>
    Task<bool> ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken = default);
}
