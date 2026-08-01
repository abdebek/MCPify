using System.Text;

namespace MCPify.Core.Auth;

/// <summary>
/// Resolves token-store provider names for auth providers.
/// When <c>providerName</c> is not set explicitly, defaults are namespaced by client id
/// (e.g. <c>OAuth:demo-client-id</c>) so multi-IdP hosts get automatic isolation.
/// </summary>
public static class AuthProviderNames
{
    public const string OAuthPrefix = "OAuth";
    public const string ClientCredentialsPrefix = "ClientCredentials";
    public const string DeviceCodePrefix = "DeviceCode";

    /// <summary>
    /// Returns <paramref name="explicitProviderName"/> if non-empty; otherwise
    /// <c>{typePrefix}:{sanitizedClientId}@{tokenHost}</c>.
    /// </summary>
    public static string Resolve(string? explicitProviderName, string typePrefix, string clientId, string? tokenEndpoint = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitProviderName))
            return explicitProviderName.Trim();

        if (string.IsNullOrWhiteSpace(typePrefix))
            throw new ArgumentException("Type prefix is required.", nameof(typePrefix));

        var sanitized = SanitizeClientId(clientId);
        var host = ExtractHost(tokenEndpoint);
        return host != null ? $"{typePrefix}:{sanitized}@{host}" : $"{typePrefix}:{sanitized}";
    }

    private static string? ExtractHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return uri.Host;
        return null;
    }

    /// <summary>
    /// Sanitizes a client id for use in a provider name (stable, filesystem-safe).
    /// </summary>
    public static string SanitizeClientId(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return "default";
        }

        var trimmed = clientId.Trim();
        var sb = new StringBuilder(trimmed.Length);
        foreach (var c in trimmed)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '@')
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('_');
            }
        }

        var result = sb.ToString().Trim('_');
        return string.IsNullOrEmpty(result) ? "default" : result;
    }
}
