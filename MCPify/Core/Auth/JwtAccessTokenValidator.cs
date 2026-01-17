using System.Text;
using System.Text.Json;

namespace MCPify.Core.Auth;

/// <summary>
/// JWT access token validator that parses and validates JWT tokens without signature verification.
/// This is suitable for tokens that have already been cryptographically validated by the authorization server.
/// Performs expiration, audience, and scope claim extraction.
/// </summary>
public class JwtAccessTokenValidator : IAccessTokenValidator
{
    private readonly TokenValidationOptions _options;
    private static readonly string[] ScopeClaimNames = { "scope", "scp", "scopes" };

    public JwtAccessTokenValidator(TokenValidationOptions options)
    {
        _options = options;
    }

    public Task<TokenValidationResult> ValidateAsync(string token, string? expectedAudience, CancellationToken cancellationToken = default)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2)
            {
                return Task.FromResult(TokenValidationResult.Failure("invalid_token", "Token is not a valid JWT format"));
            }

            var payloadJson = Base64UrlDecode(parts[1]);
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;

            // Extract claims
            var subject = GetStringClaim(root, "sub");
            var issuer = GetStringClaim(root, "iss");
            var audiences = GetAudienceClaim(root);
            var scopes = GetScopeClaim(root);
            var expiresAt = GetExpirationClaim(root);

            // Validate expiration
            if (expiresAt.HasValue)
            {
                var now = DateTimeOffset.UtcNow;
                if (expiresAt.Value.Add(_options.ClockSkew) < now)
                {
                    return Task.FromResult(TokenValidationResult.Failure("invalid_token", "Token has expired"));
                }
            }

            // Validate audience if requested
            if (_options.ValidateAudience && !string.IsNullOrEmpty(expectedAudience))
            {
                if (audiences.Count == 0 || !audiences.Any(a => string.Equals(a, expectedAudience, StringComparison.OrdinalIgnoreCase)))
                {
                    return Task.FromResult(TokenValidationResult.Failure("invalid_token", $"Token audience does not match expected value: {expectedAudience}"));
                }
            }

            return Task.FromResult(TokenValidationResult.Success(
                scopes: scopes,
                subject: subject,
                audiences: audiences,
                issuer: issuer,
                expiresAt: expiresAt
            ));
        }
        catch (JsonException)
        {
            return Task.FromResult(TokenValidationResult.Failure("invalid_token", "Token payload is not valid JSON"));
        }
        catch (FormatException)
        {
            return Task.FromResult(TokenValidationResult.Failure("invalid_token", "Token payload is not valid Base64URL"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(TokenValidationResult.Failure("invalid_token", $"Token validation failed: {ex.Message}"));
        }
    }

    private static string? GetStringClaim(JsonElement root, string claimName)
    {
        if (root.TryGetProperty(claimName, out var claim) && claim.ValueKind == JsonValueKind.String)
        {
            return claim.GetString();
        }
        return null;
    }

    private List<string> GetAudienceClaim(JsonElement root)
    {
        if (!root.TryGetProperty("aud", out var audClaim))
        {
            return new List<string>();
        }

        if (audClaim.ValueKind == JsonValueKind.String)
        {
            var value = audClaim.GetString();
            return value != null ? new List<string> { value } : new List<string>();
        }

        if (audClaim.ValueKind == JsonValueKind.Array)
        {
            var audiences = new List<string>();
            foreach (var item in audClaim.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var value = item.GetString();
                    if (value != null)
                    {
                        audiences.Add(value);
                    }
                }
            }
            return audiences;
        }

        return new List<string>();
    }

    private List<string> GetScopeClaim(JsonElement root)
    {
        // Try the configured claim name first, then fall back to common alternatives
        var claimNamesToTry = new List<string> { _options.ScopeClaimName };
        foreach (var name in ScopeClaimNames)
        {
            if (!claimNamesToTry.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                claimNamesToTry.Add(name);
            }
        }

        foreach (var claimName in claimNamesToTry)
        {
            if (!root.TryGetProperty(claimName, out var scopeClaim))
            {
                continue;
            }

            if (scopeClaim.ValueKind == JsonValueKind.String)
            {
                var value = scopeClaim.GetString();
                if (!string.IsNullOrEmpty(value))
                {
                    // Scopes are space-separated per RFC 6749
                    return value.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                }
            }

            if (scopeClaim.ValueKind == JsonValueKind.Array)
            {
                var scopes = new List<string>();
                foreach (var item in scopeClaim.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        var value = item.GetString();
                        if (!string.IsNullOrEmpty(value))
                        {
                            scopes.Add(value);
                        }
                    }
                }
                return scopes;
            }
        }

        return new List<string>();
    }

    private static DateTimeOffset? GetExpirationClaim(JsonElement root)
    {
        if (!root.TryGetProperty("exp", out var expClaim))
        {
            return null;
        }

        if (expClaim.ValueKind == JsonValueKind.Number)
        {
            var unixTime = expClaim.GetInt64();
            return DateTimeOffset.FromUnixTimeSeconds(unixTime);
        }

        return null;
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var output = input.Replace('-', '+').Replace('_', '/');
        switch (output.Length % 4)
        {
            case 0: break;
            case 2: output += "=="; break;
            case 3: output += "="; break;
            default: throw new FormatException("Illegal base64url string!");
        }
        return Convert.FromBase64String(output);
    }
}
