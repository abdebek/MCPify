using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using MCPify.Core;
using MCPify.Core.Auth;

namespace MCPify.Core.Auth.OAuth;

public class OAuthAuthorizationCodeAuthentication : IAuthenticationProvider
{
    private readonly string _clientId;
    private readonly string? _clientSecret;
    private readonly string _authorizationEndpoint;
    private readonly string _tokenEndpoint;
    private readonly string _scope;
    private readonly ISecureTokenStore _secureTokenStore;
    private readonly IMcpContextAccessor _mcpContextAccessor;
    private readonly HttpClient _httpClient;
    private readonly string? _redirectUri;
    private readonly Action<string>? _openBrowserAction;
    private readonly bool _usePkce;
    private readonly Action<string>? _authorizationUrlEmitter;

    public OAuthAuthorizationCodeAuthentication(
        string clientId,
        string authorizationEndpoint,
        string tokenEndpoint,
        string scope,
        ISecureTokenStore secureTokenStore,
        IMcpContextAccessor mcpContextAccessor,
        string? clientSecret = null,
        HttpClient? httpClient = null,
        string? redirectUri = null,
        Action<string>? openBrowserAction = null,
        bool usePkce = false,
        Action<string>? authorizationUrlEmitter = null)
    {
        _clientId = clientId;
        _authorizationEndpoint = authorizationEndpoint;
        _tokenEndpoint = tokenEndpoint;
        _scope = scope;
        _secureTokenStore = secureTokenStore;
        _mcpContextAccessor = mcpContextAccessor;
        _clientSecret = clientSecret;
        _httpClient = httpClient ?? new HttpClient();
        _redirectUri = redirectUri;
        _openBrowserAction = openBrowserAction;
        _usePkce = usePkce;
        _authorizationUrlEmitter = authorizationUrlEmitter;
    }

    public string BuildAuthorizationUrl(string sessionId)
    {
        var redirectUri = _redirectUri ?? throw new InvalidOperationException("redirectUri must be configured for auth URL generation.");

        var state = CreateSignedState(sessionId, redirectUri, out var nonce);

        (string CodeVerifier, string CodeChallenge)? pkce = null;
        if (_usePkce)
        {
            pkce = GeneratePkcePair();
            _secureTokenStore.SaveTokenAsync(sessionId, _pkceStorePrefix + nonce, new TokenData(pkce.Value.CodeVerifier, null, null), CancellationToken.None).GetAwaiter().GetResult();
        }
        
        var query = HttpUtility.ParseQueryString("");
        query["response_type"] = "code";
        query["client_id"] = _clientId;
        query["redirect_uri"] = redirectUri;
        query["scope"] = _scope;
        query["state"] = state;
        if (_usePkce && pkce.HasValue)
        {
            query["code_challenge"] = pkce.Value.CodeChallenge;
            query["code_challenge_method"] = "S256";
        }

        return $"{_authorizationEndpoint}?{query}";
    }

    public async Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(_mcpContextAccessor.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _mcpContextAccessor.AccessToken);
            return;
        }

        var sessionId = _mcpContextAccessor.SessionId
            ?? throw new InvalidOperationException("SessionId not set in MCP context. Cannot apply authentication.");

        var tokenData = await _secureTokenStore.GetTokenAsync(sessionId, _oauthProviderName, cancellationToken);

        if (tokenData != null && (!tokenData.ExpiresAt.HasValue || tokenData.ExpiresAt.Value > DateTimeOffset.UtcNow.AddMinutes(1)))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenData.AccessToken);
            return;
        }

        if (tokenData?.RefreshToken != null)
        {
            try
            {
                tokenData = await RefreshTokenAsync(tokenData.RefreshToken, sessionId, cancellationToken);
                await _secureTokenStore.SaveTokenAsync(sessionId, _oauthProviderName, tokenData, cancellationToken);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenData.AccessToken);
                return;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Token refresh failed for session {sessionId}: {ex.Message}");
                await _secureTokenStore.DeleteTokenAsync(sessionId, _oauthProviderName, cancellationToken);
            }
        }

        throw new InvalidOperationException($"No valid token found or refresh failed for session '{sessionId}'. Run the login tool to authenticate first.");
    }

    public async Task<TokenData> HandleAuthorizationCallbackAsync(string code, string stateParam, CancellationToken cancellationToken = default)
    {
        var oauthState = ValidateAndExtractSignedState(stateParam);
        var sessionId = oauthState.SessionId!;
        var nonce = oauthState.Nonce!;
        var redirectUri = oauthState.RedirectUri!;

        _mcpContextAccessor.SessionId = sessionId; 

        string? codeVerifier = null;
        if (_usePkce)
        {
            var pkceTokenData = await _secureTokenStore.GetTokenAsync(sessionId, _pkceStorePrefix + nonce, cancellationToken)
                ?? throw new InvalidOperationException($"PKCE verifier not found for session '{sessionId}' and nonce '{nonce}'. Login process invalid or expired.");
            codeVerifier = pkceTokenData.AccessToken;
        }

        var tokenData = await ExchangeCodeForTokenAsync(code, redirectUri, codeVerifier, cancellationToken);
        
        await _secureTokenStore.SaveTokenAsync(sessionId, _oauthProviderName, tokenData, cancellationToken);

        if (_usePkce)
        {
            await _secureTokenStore.DeleteTokenAsync(sessionId, _pkceStorePrefix + nonce, cancellationToken);
        }

        return tokenData;
    }

    private async Task<TokenData> ExchangeCodeForTokenAsync(string code, string redirectUri, string? codeVerifier, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            { "grant_type", "authorization_code" },
            { "client_id", _clientId },
            { "code", code },
            { "redirect_uri", redirectUri }
        };

        if (!string.IsNullOrEmpty(codeVerifier))
        {
            form["code_verifier"] = codeVerifier;
        }

        if (!string.IsNullOrEmpty(_clientSecret))
        {
            form["client_secret"] = _clientSecret;
        }

        var content = new FormUrlEncodedContent(form);

        var response = await _httpClient.PostAsync(_tokenEndpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var accessToken = root.GetProperty("access_token").GetString() 
            ?? throw new Exception("No access_token in response");
        
        var refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        
        var expiresAt = root.TryGetProperty("expires_in", out var exp) 
            ? (DateTimeOffset?)DateTimeOffset.UtcNow.AddSeconds(exp.GetInt32()) 
            : null;

        return new TokenData(accessToken, refreshToken, expiresAt);
    }

    private async Task<TokenData> RefreshTokenAsync(string refreshToken, string sessionId, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            { "grant_type", "refresh_token" },
            { "client_id", _clientId },
            { "refresh_token", refreshToken }
        };

        if (!string.IsNullOrEmpty(_clientSecret))
        {
            form["client_secret"] = _clientSecret;
        }

        var content = new FormUrlEncodedContent(form);

        var response = await _httpClient.PostAsync(_tokenEndpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var accessToken = root.GetProperty("access_token").GetString() 
            ?? throw new Exception("No access_token in response");
        
        var newRefreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : refreshToken;
        
        var expiresAt = root.TryGetProperty("expires_in", out var exp) 
            ? (DateTimeOffset?)DateTimeOffset.UtcNow.AddSeconds(exp.GetInt32()) 
            : null;

        return new TokenData(accessToken, newRefreshToken, expiresAt);
    }


    private static (string CodeVerifier, string CodeChallenge) GeneratePkcePair()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        var verifier = Base64UrlEncode(bytes);

        using var sha = SHA256.Create();
        var challenge = Base64UrlEncode(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                url = url.Replace("&", "^&");
                Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
        }
    }

    private readonly string _stateSecret = "A_VERY_LONG_AND_SECURE_SECRET_KEY_FOR_HMAC_SIGNING"; 
    private const string _oauthProviderName = "OAuth";
    private const string _pkceStorePrefix = "pkce_";

    private string CreateSignedState(string sessionId, string redirectUri, out string nonce)
    {
        nonce = Guid.NewGuid().ToString("N");
        var oauthState = new OAuthState
        {
            Nonce = nonce,
            SessionId = sessionId,
            RedirectUri = redirectUri,
            ProviderName = _oauthProviderName
        };

        var jsonState = JsonSerializer.Serialize(oauthState);
        var signature = SignData(jsonState, _stateSecret);

        return $"{Base64UrlEncode(Encoding.UTF8.GetBytes(jsonState))}.{Base64UrlEncode(signature)}";
    }

    private OAuthState ValidateAndExtractSignedState(string signedState)
    {
        var parts = signedState.Split('.');
        if (parts.Length != 2)
        {
            throw new CryptographicException("Invalid signed state format.");
        }

        var jsonStateBytes = Base64UrlDecode(parts[0]);
        var signature = Base64UrlDecode(parts[1]);

        var jsonState = Encoding.UTF8.GetString(jsonStateBytes);

        if (!VerifySignature(jsonState, signature, _stateSecret))
        {
            throw new CryptographicException("Invalid state signature.");
        }

        var oauthState = JsonSerializer.Deserialize<OAuthState>(jsonState)
            ?? throw new CryptographicException("Failed to deserialize OAuth state.");

        if (string.IsNullOrEmpty(oauthState.SessionId) || string.IsNullOrEmpty(oauthState.Nonce))
        {
            throw new CryptographicException("OAuth state missing required fields.");
        }

        return oauthState;
    }

    private static byte[] SignData(string data, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    private static bool VerifySignature(string data, byte[] signature, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var computedSignature = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return CryptographicOperations.FixedTimeEquals(computedSignature, signature);
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

    private class OAuthState
    {
        public string? Nonce { get; set; }
        public string? SessionId { get; set; }
        public string? RedirectUri { get; set; }
        public string? ProviderName { get; set; }
    }
}