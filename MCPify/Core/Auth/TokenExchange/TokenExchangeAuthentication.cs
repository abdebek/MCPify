using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MCPify.Core.Auth.OAuth;

namespace MCPify.Core.Auth.TokenExchange;

/// <summary>
/// Implements RFC 8693 OAuth 2.0 Token Exchange.
/// Exchanges the MCP client's access token for an upstream API token.
/// </summary>
public class TokenExchangeAuthentication : IAuthenticationProvider
{
    private const string GrantType = "urn:ietf:params:oauth:grant-type:token-exchange";
    private const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";

    private readonly string _tokenEndpoint;
    private readonly string _clientId;
    private readonly string? _clientSecret;
    private readonly string? _scope;
    private readonly string? _resource;
    private readonly string? _audience;
    private readonly ISecureTokenStore _tokenStore;
    private readonly IMcpContextAccessor _mcpContextAccessor;
    private readonly HttpClient _httpClient;
    private readonly string _providerName;

    public TokenExchangeAuthentication(
        string tokenEndpoint,
        string clientId,
        string? clientSecret,
        string? scope,
        string? resource,
        string? audience,
        ISecureTokenStore tokenStore,
        IMcpContextAccessor mcpContextAccessor,
        HttpClient? httpClient = null,
        string providerName = "TokenExchange")
    {
        ArgumentException.ThrowIfNullOrEmpty(tokenEndpoint);
        ArgumentException.ThrowIfNullOrEmpty(clientId);

        _tokenEndpoint = tokenEndpoint;
        _clientId = clientId;
        _clientSecret = clientSecret;
        _scope = scope;
        _resource = resource;
        _audience = audience;
        _tokenStore = tokenStore ?? throw new ArgumentNullException(nameof(tokenStore));
        _mcpContextAccessor = mcpContextAccessor ?? throw new ArgumentNullException(nameof(mcpContextAccessor));
        _httpClient = httpClient ?? HttpClientFallback.Create(nameof(TokenExchangeAuthentication));
        _providerName = providerName;
    }

    public async Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        var sessionId = _mcpContextAccessor.SessionId ?? "default";
        var accessToken = await GetOrExchangeTokenAsync(sessionId, cancellationToken);

        if (!string.IsNullOrEmpty(accessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }
    }

    internal async Task<string?> GetOrExchangeTokenAsync(string sessionId, CancellationToken cancellationToken)
    {
        // Check cached token
        var cached = await _tokenStore.GetTokenAsync(sessionId, _providerName, cancellationToken);
        if (cached != null && cached.ExpiresAt.HasValue && cached.ExpiresAt.Value > DateTimeOffset.UtcNow.AddSeconds(30))
        {
            return cached.AccessToken;
        }

        // Get the subject token from the MCP context
        var subjectToken = _mcpContextAccessor.AccessToken;
        if (string.IsNullOrEmpty(subjectToken))
        {
            return null;
        }

        // Strip "Bearer " prefix if present
        if (subjectToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            subjectToken = subjectToken["Bearer ".Length..];
        }

        // Perform the exchange
        var exchangedToken = await ExchangeTokenAsync(subjectToken, cancellationToken);
        if (exchangedToken == null)
        {
            return null;
        }

        // Cache
        await _tokenStore.SaveTokenAsync(sessionId, _providerName, exchangedToken, cancellationToken);
        return exchangedToken.AccessToken;
    }

    internal async Task<TokenData?> ExchangeTokenAsync(string subjectToken, CancellationToken cancellationToken)
    {
        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = GrantType,
            ["subject_token"] = subjectToken,
            ["subject_token_type"] = AccessTokenType,
            ["client_id"] = _clientId
        };

        if (!string.IsNullOrEmpty(_clientSecret))
        {
            parameters["client_secret"] = _clientSecret;
        }

        if (!string.IsNullOrEmpty(_scope))
        {
            parameters["scope"] = _scope;
        }

        if (!string.IsNullOrEmpty(_resource))
        {
            parameters["resource"] = _resource;
        }

        if (!string.IsNullOrEmpty(_audience))
        {
            parameters["audience"] = _audience;
        }

        using var content = new FormUrlEncodedContent(parameters);
        using var response = await _httpClient.PostAsync(_tokenEndpoint, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Token exchange failed at '{_tokenEndpoint}': HTTP {(int)response.StatusCode} {response.ReasonPhrase}. Response: {errorBody}");
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("access_token", out var accessTokenProp))
        {
            throw new InvalidOperationException("Token exchange response did not contain 'access_token'.");
        }

        var accessToken = accessTokenProp.GetString();
        if (string.IsNullOrEmpty(accessToken))
        {
            throw new InvalidOperationException("Token exchange response contained an empty 'access_token'.");
        }

        DateTimeOffset? expiresAt = null;
        if (root.TryGetProperty("expires_in", out var expiresInProp) && expiresInProp.TryGetInt32(out var expiresIn))
        {
            expiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn);
        }

        string? refreshToken = null;
        if (root.TryGetProperty("refresh_token", out var refreshProp))
        {
            refreshToken = refreshProp.GetString();
        }

        return new TokenData(accessToken, refreshToken, expiresAt);
    }
}
