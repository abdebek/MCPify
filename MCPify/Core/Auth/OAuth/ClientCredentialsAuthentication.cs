using System.Net.Http.Headers;
using System.Text.Json;
using MCPify.Core;
using MCPify.Core.Auth;

namespace MCPify.Core.Auth.OAuth;

public class ClientCredentialsAuthentication : IAuthenticationProvider
{
    private readonly string _clientId;
    private readonly string _clientSecret;
    private readonly string _tokenEndpoint;
    private readonly string _scope;
    private readonly ISecureTokenStore _secureTokenStore;
    private readonly IMcpContextAccessor _mcpContextAccessor;
    private readonly HttpClient _httpClient;
    private const string _clientCredentialsProviderName = "ClientCredentials";

    public ClientCredentialsAuthentication(
        string clientId,
        string clientSecret,
        string tokenEndpoint,
        string scope,
        ISecureTokenStore secureTokenStore,
        IMcpContextAccessor mcpContextAccessor,
        HttpClient? httpClient = null)
    {
        _clientId = clientId;
        _clientSecret = clientSecret;
        _tokenEndpoint = tokenEndpoint;
        _scope = scope;
        _secureTokenStore = secureTokenStore;
        _mcpContextAccessor = mcpContextAccessor;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        var sessionId = _mcpContextAccessor.SessionId
            ?? throw new InvalidOperationException("SessionId not set in MCP context. Cannot apply authentication.");

        var tokenData = await _secureTokenStore.GetTokenAsync(sessionId, _clientCredentialsProviderName, cancellationToken);

        if (tokenData != null && (!tokenData.ExpiresAt.HasValue || tokenData.ExpiresAt.Value > DateTimeOffset.UtcNow.AddMinutes(1)))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenData.AccessToken);
            return;
        }

        tokenData = await RequestTokenAsync(cancellationToken);
        await _secureTokenStore.SaveTokenAsync(sessionId, _clientCredentialsProviderName, tokenData, cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenData.AccessToken);
    }

    private async Task<TokenData> RequestTokenAsync(CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            { "grant_type", "client_credentials" },
            { "client_id", _clientId },
            { "client_secret", _clientSecret },
            { "scope", _scope }
        };

        var content = new FormUrlEncodedContent(form);
        var response = await _httpClient.PostAsync(_tokenEndpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var accessToken = root.GetProperty("access_token").GetString()
            ?? throw new Exception("No access_token in response");

        var expiresAt = root.TryGetProperty("expires_in", out var exp)
            ? (DateTimeOffset?)DateTimeOffset.UtcNow.AddSeconds(exp.GetInt32())
            : null;

        return new TokenData(accessToken, null, expiresAt);
    }
}