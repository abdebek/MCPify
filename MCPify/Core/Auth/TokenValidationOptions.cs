namespace MCPify.Core.Auth;

/// <summary>
/// Configuration options for token validation behavior.
/// Token validation is opt-in for backward compatibility.
/// </summary>
public class TokenValidationOptions
{
    /// <summary>
    /// When true, enables JWT token validation including expiration, audience, and scope checks.
    /// Defaults to false for backward compatibility.
    /// </summary>
    public bool EnableJwtValidation { get; set; } = false;

    /// <summary>
    /// When true, validates that the token's 'aud' claim matches the expected audience (resource URL).
    /// Only applies when <see cref="EnableJwtValidation"/> is true.
    /// </summary>
    public bool ValidateAudience { get; set; } = false;

    /// <summary>
    /// When true, validates that the token contains required scopes for the requested operation.
    /// Only applies when <see cref="EnableJwtValidation"/> is true.
    /// </summary>
    public bool ValidateScopes { get; set; } = false;

    /// <summary>
    /// The expected audience value for token validation.
    /// If not set, defaults to the resource URL.
    /// </summary>
    public string? ExpectedAudience { get; set; }

    /// <summary>
    /// Default scopes required for all endpoints when scope validation is enabled.
    /// Specific endpoints can override this with <see cref="McpifyOptions.ScopeRequirements"/>.
    /// </summary>
    public List<string> DefaultRequiredScopes { get; set; } = new();

    /// <summary>
    /// When true, automatically requires all scopes defined in <see cref="OAuth2Configuration.Scopes"/>
    /// from the <see cref="OAuthConfigurationStore"/> in addition to <see cref="DefaultRequiredScopes"/>.
    /// Defaults to false for backward compatibility.
    /// </summary>
    public bool RequireOAuthConfiguredScopes { get; set; } = false;

    /// <summary>
    /// The claim name used for scopes in the JWT token.
    /// Common values: "scope", "scp", "scopes".
    /// Defaults to "scope".
    /// </summary>
    public string ScopeClaimName { get; set; } = "scope";

    /// <summary>
    /// Allowed clock skew for token expiration validation.
    /// Defaults to 5 minutes.
    /// </summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromMinutes(5);
}
