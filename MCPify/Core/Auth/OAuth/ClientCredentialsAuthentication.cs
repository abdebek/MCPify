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
    private readonly string? _resourceUrl; // RFC 8707 resource parameter
    private const string _clientCredentialsProviderName = "ClientCredentials";

    public ClientCredentialsAuthentication(
        string clientId,
        string clientSecret,
        string tokenEndpoint,
        string scope,
        ISecureTokenStore secureTokenStore,
        IMcpContextAccessor mcpContextAccessor,
        HttpClient? httpClient = null,
        string? resourceUrl = null)
    {
        _clientId = clientId;
        _clientSecret = clientSecret;
        _tokenEndpoint = tokenEndpoint;
        _scope = scope;
        _secureTokenStore = secureTokenStore;
        _mcpContextAccessor = mcpContextAccessor;
        _httpClient = httpClient ?? new HttpClient();
        _resourceUrl = resourceUrl;
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
        var content = FormUrlEncoded.Create()
            .Add("grant_type", "client_credentials")
            .Add("client_id", _clientId)
            .Add("client_secret", _clientSecret)
            .Add("scope", _scope)
            .AddIfNotEmpty("resource", _resourceUrl)  // RFC 8707
            .ToContent();
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