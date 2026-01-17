using System.Net.Http.Json;
using MCPify.Core.Auth.OAuth;
using MCPify.Core;
using MCPify.Core.Auth;

namespace MCPify.Core.Auth.DeviceCode;

public class DeviceCodeAuthentication : IAuthenticationProvider
{
    private readonly string _clientId;
    private readonly string _deviceCodeEndpoint;
    private readonly string _tokenEndpoint;
    private readonly string _scope;
    private readonly ISecureTokenStore _secureTokenStore;
    private readonly IMcpContextAccessor _mcpContextAccessor;
    private readonly HttpClient _httpClient;
    private readonly Func<string, string, Task> _userPrompt;
    private readonly string? _resourceUrl; // RFC 8707 resource parameter
    private const string _deviceCodeProviderName = "DeviceCode";

    public DeviceCodeAuthentication(
        string clientId,
        string deviceCodeEndpoint,
        string tokenEndpoint,
        string scope,
        ISecureTokenStore secureTokenStore,
        IMcpContextAccessor mcpContextAccessor,
        Func<string, string, Task> userPrompt,
        HttpClient? httpClient = null,
        string? resourceUrl = null)
    {
        _clientId = clientId;
        _deviceCodeEndpoint = deviceCodeEndpoint;
        _tokenEndpoint = tokenEndpoint;
        _scope = scope;
        _secureTokenStore = secureTokenStore;
        _mcpContextAccessor = mcpContextAccessor;
        _userPrompt = userPrompt;
        _httpClient = httpClient ?? new HttpClient();
        _resourceUrl = resourceUrl;
    }

    public async Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        var sessionId = _mcpContextAccessor.SessionId
            ?? throw new InvalidOperationException("SessionId not set in MCP context. Cannot apply authentication.");

        var tokenData = await _secureTokenStore.GetTokenAsync(sessionId, _deviceCodeProviderName, cancellationToken);

        if (tokenData != null && (!tokenData.ExpiresAt.HasValue || tokenData.ExpiresAt.Value > DateTimeOffset.UtcNow.AddMinutes(1)))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenData.AccessToken);
            return;
        }

        if (tokenData?.RefreshToken != null)
        {
            try
            {
                tokenData = await RefreshTokenAsync(tokenData.RefreshToken, cancellationToken);
                await _secureTokenStore.SaveTokenAsync(sessionId, _deviceCodeProviderName, tokenData, cancellationToken);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenData.AccessToken);
                return;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Token refresh failed for session {sessionId} (Device Code): {ex.Message}");
                await _secureTokenStore.DeleteTokenAsync(sessionId, _deviceCodeProviderName, cancellationToken);
            }
        }

        tokenData = await PerformDeviceLoginAsync(cancellationToken);
        await _secureTokenStore.SaveTokenAsync(sessionId, _deviceCodeProviderName, tokenData, cancellationToken);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenData.AccessToken);
    }

    private async Task<TokenData> PerformDeviceLoginAsync(CancellationToken cancellationToken)
    {
        var codeRequest = FormUrlEncoded.Create()
            .Add("client_id", _clientId)
            .Add("scope", _scope)
            .AddIfNotEmpty("resource", _resourceUrl)  // RFC 8707
            .ToContent();

        var codeResponse = await _httpClient.PostAsync(_deviceCodeEndpoint, codeRequest, cancellationToken);
        codeResponse.EnsureSuccessStatusCode();
        var codeData = await codeResponse.Content.ReadFromJsonAsync<DeviceCodeResponse>(cancellationToken: cancellationToken);

        if (codeData == null) throw new Exception("Invalid device code response");

        await _userPrompt(codeData.verification_uri, codeData.user_code);

        var interval = codeData.interval > 0 ? codeData.interval : 5;
        var expiresAt = DateTime.UtcNow.AddSeconds(codeData.expires_in);

        while (DateTime.UtcNow < expiresAt)
        {
            await Task.Delay(interval * 1000, cancellationToken);

            var tokenRequest = FormUrlEncoded.Create()
                .Add("grant_type", "urn:ietf:params:oauth:grant-type:device_code")
                .Add("client_id", _clientId)
                .Add("device_code", codeData.device_code)
                .AddIfNotEmpty("resource", _resourceUrl)  // RFC 8707
                .ToContent();

            var tokenResponse = await _httpClient.PostAsync(_tokenEndpoint, tokenRequest, cancellationToken);
            
            if (tokenResponse.IsSuccessStatusCode)
            {
                var tokenResult = await tokenResponse.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken);
                return new TokenData(
                    tokenResult!.access_token, 
                    tokenResult.refresh_token, 
                    DateTimeOffset.UtcNow.AddSeconds(tokenResult.expires_in)
                );
            }

            var errorContent = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
            if (!errorContent.Contains("authorization_pending"))
            {
                throw new Exception($"Device flow failed: {errorContent}");
            }
        }

        throw new Exception("Device code expired");
    }

    private async Task<TokenData> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var content = FormUrlEncoded.Create()
            .Add("grant_type", "refresh_token")
            .Add("client_id", _clientId)
            .Add("refresh_token", refreshToken)
            .AddIfNotEmpty("resource", _resourceUrl)  // RFC 8707
            .ToContent();

        var response = await _httpClient.PostAsync(_tokenEndpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();
        
        var result = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken);
        
        return new TokenData(
            result!.access_token, 
            result.refresh_token ?? refreshToken, 
            DateTimeOffset.UtcNow.AddSeconds(result.expires_in)
        );
    }

    private record DeviceCodeResponse(string device_code, string user_code, string verification_uri, int expires_in, int interval);
    private record TokenResponse(string access_token, string refresh_token, int expires_in);
}