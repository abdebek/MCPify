namespace MCPify.Core;

/// <summary>
/// Configuration options for RFC 8693 OAuth 2.0 Token Exchange.
/// </summary>
public class TokenExchangeOptions
{
    /// <summary>
    /// The token endpoint URL of the authorization server. Required.
    /// </summary>
    public string? TokenEndpoint { get; set; }

    /// <summary>
    /// The OAuth 2.0 client identifier. Required.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// The OAuth 2.0 client secret. Optional — omit for public clients.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Space-separated list of scopes to request for the exchanged token. Optional.
    /// </summary>
    public string? Scope { get; set; }

    /// <summary>
    /// The target resource indicator per RFC 8707. Optional.
    /// </summary>
    public string? Resource { get; set; }

    /// <summary>
    /// The logical name of the target service per RFC 8693. Optional.
    /// </summary>
    public string? Audience { get; set; }

    /// <summary>
    /// Custom <see cref="HttpClient"/> to use for the exchange request.
    /// If null, a default client is created.
    /// </summary>
    public HttpClient? HttpClient { get; set; }

    /// <summary>
    /// Key used to store/retrieve exchanged tokens in the <see cref="Auth.ISecureTokenStore"/>.
    /// Defaults to <c>"TokenExchange"</c>.
    /// </summary>
    public string ProviderName { get; set; } = "TokenExchange";
}
