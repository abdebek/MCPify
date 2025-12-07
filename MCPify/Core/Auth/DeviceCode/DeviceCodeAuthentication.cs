using System.Net.Http.Json;
using MCPify.Core.Auth.OAuth;

namespace MCPify.Core.Auth.DeviceCode;

public class DeviceCodeAuthentication : IAuthenticationProvider
{
    private readonly string _clientId;
    private readonly string _deviceCodeEndpoint;
    private readonly string _tokenEndpoint;
    private readonly string _scope;
    private readonly ITokenStore _tokenStore;
    private readonly HttpClient _httpClient;
    private readonly Func<string, string, Task> _userPrompt;

    public DeviceCodeAuthentication(
        string clientId,
        string deviceCodeEndpoint,
        string tokenEndpoint,
        string scope,
        ITokenStore tokenStore,
        Func<string, string, Task> userPrompt,
        HttpClient? httpClient = null)
    {
        _clientId = clientId;
        _deviceCodeEndpoint = deviceCodeEndpoint;
        _tokenEndpoint = tokenEndpoint;
        _scope = scope;
        _tokenStore = tokenStore;
        _userPrompt = userPrompt;
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        var tokenData = await _tokenStore.GetTokenAsync(cancellationToken);

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
                await _tokenStore.SaveTokenAsync(tokenData, cancellationToken);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenData.AccessToken);
                return;
            }
            catch
            {
                // Refresh failed, fall back to full login
            }
        }

        tokenData = await PerformDeviceLoginAsync(cancellationToken);
        await _tokenStore.SaveTokenAsync(tokenData, cancellationToken);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tokenData.AccessToken);
    }

    private async Task<TokenData> PerformDeviceLoginAsync(CancellationToken cancellationToken)
    {
        // 1. Request Device Code
        var codeRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "client_id", _clientId },
            { "scope", _scope }
        });

        var codeResponse = await _httpClient.PostAsync(_deviceCodeEndpoint, codeRequest, cancellationToken);
        codeResponse.EnsureSuccessStatusCode();
        var codeData = await codeResponse.Content.ReadFromJsonAsync<DeviceCodeResponse>(cancellationToken: cancellationToken);

        if (codeData == null) throw new Exception("Invalid device code response");

        // 2. Prompt User
        await _userPrompt(codeData.verification_uri, codeData.user_code);

        // 3. Poll for Token
        var interval = codeData.interval > 0 ? codeData.interval : 5;
        var expiresAt = DateTime.UtcNow.AddSeconds(codeData.expires_in);

        while (DateTime.UtcNow < expiresAt)
        {
            await Task.Delay(interval * 1000, cancellationToken);

            var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "grant_type", "urn:ietf:params:oauth:grant-type:device_code" },
                { "client_id", _clientId },
                { "device_code", codeData.device_code }
            });

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
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "grant_type", "refresh_token" },
            { "client_id", _clientId },
            { "refresh_token", refreshToken }
        });

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
