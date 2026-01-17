namespace MCPify.Core.Auth;

/// <summary>
/// Result of access token validation.
/// </summary>
public class TokenValidationResult
{
    /// <summary>
    /// Whether the token is valid.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Error code when validation fails (e.g., "invalid_token", "expired_token").
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Human-readable description of the error.
    /// </summary>
    public string? ErrorDescription { get; init; }

    /// <summary>
    /// Scopes extracted from the token.
    /// </summary>
    public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The subject (sub) claim from the token.
    /// </summary>
    public string? Subject { get; init; }

    /// <summary>
    /// The audiences (aud) claim from the token.
    /// </summary>
    public IReadOnlyList<string> Audiences { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The issuer (iss) claim from the token.
    /// </summary>
    public string? Issuer { get; init; }

    /// <summary>
    /// Token expiration time if present.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static TokenValidationResult Success(
        IReadOnlyList<string>? scopes = null,
        string? subject = null,
        IReadOnlyList<string>? audiences = null,
        string? issuer = null,
        DateTimeOffset? expiresAt = null)
    {
        return new TokenValidationResult
        {
            IsValid = true,
            Scopes = scopes ?? Array.Empty<string>(),
            Subject = subject,
            Audiences = audiences ?? Array.Empty<string>(),
            Issuer = issuer,
            ExpiresAt = expiresAt
        };
    }

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    public static TokenValidationResult Failure(string errorCode, string errorDescription)
    {
        return new TokenValidationResult
        {
            IsValid = false,
            ErrorCode = errorCode,
            ErrorDescription = errorDescription
        };
    }
}
