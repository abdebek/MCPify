namespace MCPify.Core.Auth;

/// <summary>
/// Interface for validating access tokens.
/// </summary>
public interface IAccessTokenValidator
{
    /// <summary>
    /// Validates an access token and returns the validation result.
    /// </summary>
    /// <param name="token">The access token to validate.</param>
    /// <param name="expectedAudience">Optional expected audience value. If null, audience validation is skipped.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="TokenValidationResult"/> containing the validation outcome and extracted claims.</returns>
    Task<TokenValidationResult> ValidateAsync(string token, string? expectedAudience, CancellationToken cancellationToken = default);
}
