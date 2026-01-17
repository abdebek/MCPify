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

using MCPify.Core.Session;

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
    private readonly string _stateSecret;
    private readonly bool _allowDefaultSessionFallback;
    private readonly ISessionMap? _sessionMap; // Optional dependency for Lazy Auth
    private readonly string? _resourceUrl; // RFC 8707 resource parameter
    private const string _oauthProviderName = "OAuth";
    private const string _pkceStorePrefix = "pkce_";

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
        Action<string>? authorizationUrlEmitter = null,
        string? stateSecret = null,
        bool allowDefaultSessionFallback = false,
        ISessionMap? sessionMap = null,
        string? resourceUrl = null)
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
        _stateSecret = stateSecret ?? "A_VERY_LONG_AND_SECURE_SECRET_KEY_FOR_HMAC_SIGNING";
        _allowDefaultSessionFallback = allowDefaultSessionFallback;
        _sessionMap = sessionMap;
        _resourceUrl = resourceUrl;
    }

    public virtual string BuildAuthorizationUrl(string sessionId)
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
        // RFC 8707: Add resource parameter if configured
        if (!string.IsNullOrEmpty(_resourceUrl))
        {
            query["resource"] = _resourceUrl;
        }

        return $"{_authorizationEndpoint}?{query}";
    }

    public virtual async Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(_mcpContextAccessor.AccessToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _mcpContextAccessor.AccessToken);
            return;
        }

        var sessionId = _mcpContextAccessor.SessionId;

        if (string.IsNullOrEmpty(sessionId))
        {
            throw new InvalidOperationException("SessionId not set in MCP context. Cannot apply authentication.");
        }

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

    public virtual async Task<TokenData> HandleAuthorizationCallbackAsync(string code, string stateParam, CancellationToken cancellationToken = default)
    {
        var oauthState = ValidateAndExtractSignedState(stateParam);
        var sessionHandle = oauthState.SessionId!;
        var nonce = oauthState.Nonce!;
        var redirectUri = oauthState.RedirectUri!;

        // Default to using the handle as the storage key
        var storageKey = sessionHandle;

        string? codeVerifier = null;
        if (_usePkce)
        {
            // PKCE verifier was stored under the session HANDLE (Temp ID)
            var pkceTokenData = await _secureTokenStore.GetTokenAsync(sessionHandle, _pkceStorePrefix + nonce, cancellationToken)
                ?? throw new InvalidOperationException($"PKCE verifier not found for session '{sessionHandle}' and nonce '{nonce}'. Login process invalid or expired.");
            codeVerifier = pkceTokenData.AccessToken;
        }

        var tokenData = await ExchangeCodeForTokenAsync(code, redirectUri, codeVerifier, cancellationToken);

        // Session Upgrade Logic:
        // If we have an ID Token, try to extract the user's stable identity (sub/email).
        var idToken = ExtractIdToken(tokenData);
        if (!string.IsNullOrEmpty(idToken))
        {
            var principalId = ExtractPrincipalFromIdToken(idToken);
            if (!string.IsNullOrEmpty(principalId) && _sessionMap != null)
            {
                // Upgrade the session mapping: sessionHandle points to principalId
                _sessionMap.UpgradeSession(sessionHandle, principalId);
                // Future storage should use the principalId
                storageKey = principalId;
                // Update the current context to reflect the change immediately
                _mcpContextAccessor.SessionId = principalId;
            }
        }
        else
        {
            // Ensure context is set to the handle if no upgrade happened
             _mcpContextAccessor.SessionId = sessionHandle;
        }
        
        await _secureTokenStore.SaveTokenAsync(storageKey, _oauthProviderName, tokenData, cancellationToken);

        if (_usePkce)
        {
            await _secureTokenStore.DeleteTokenAsync(sessionHandle, _pkceStorePrefix + nonce, cancellationToken);
        }

        return tokenData;
    }

    private string? ExtractIdToken(TokenData tokenData)
    {
        return tokenData.IdToken;
    }

    private string? ExtractPrincipalFromIdToken(string idToken)
    {
        try
        {
            var parts = idToken.Split('.');
            if (parts.Length < 2) return null;
            
            var payload = parts[1];
            var jsonBytes = Base64UrlDecode(payload);
            using var doc = JsonDocument.Parse(jsonBytes);
            
            if (doc.RootElement.TryGetProperty("sub", out var sub))
            {
                return sub.GetString();
            }
        }
        catch 
        { 
            // Ignore parsing failures
        }
        return null;
    }

    private async Task<TokenData> ExchangeCodeForTokenAsync(string code, string redirectUri, string? codeVerifier, CancellationToken cancellationToken)
    {
        var content = FormUrlEncoded.Create()
            .Add("grant_type", "authorization_code")
            .Add("client_id", _clientId)
            .Add("code", code)
            .Add("redirect_uri", redirectUri)
            .AddIfNotEmpty("code_verifier", codeVerifier)
            .AddIfNotEmpty("client_secret", _clientSecret)
            .AddIfNotEmpty("resource", _resourceUrl)  // RFC 8707
            .ToContent();

        var response = await _httpClient.PostAsync(_tokenEndpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var accessToken = root.GetProperty("access_token").GetString() 
            ?? throw new Exception("No access_token in response");
        
        var refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        var idToken = root.TryGetProperty("id_token", out var it) ? it.GetString() : null;
        
        var expiresAt = root.TryGetProperty("expires_in", out var exp) 
            ? (DateTimeOffset?)DateTimeOffset.UtcNow.AddSeconds(exp.GetInt32()) 
            : null;

        return new TokenData(accessToken, refreshToken, expiresAt, idToken);
    }

    private async Task<TokenData> RefreshTokenAsync(string refreshToken, string sessionId, CancellationToken cancellationToken)
    {
        var content = FormUrlEncoded.Create()
            .Add("grant_type", "refresh_token")
            .Add("client_id", _clientId)
            .Add("refresh_token", refreshToken)
            .AddIfNotEmpty("client_secret", _clientSecret)
            .AddIfNotEmpty("resource", _resourceUrl)  // RFC 8707
            .ToContent();

        var response = await _httpClient.PostAsync(_tokenEndpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var accessToken = root.GetProperty("access_token").GetString()
            ?? throw new Exception("No access_token in response");

        var newRefreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        var idToken = root.TryGetProperty("id_token", out var it) ? it.GetString() : null;

        var expiresAt = root.TryGetProperty("expires_in", out var exp) 
            ? (DateTimeOffset?)DateTimeOffset.UtcNow.AddSeconds(exp.GetInt32()) 
            : null;

        return new TokenData(accessToken, newRefreshToken, expiresAt, idToken);
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
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_stateSecret));
            var computed = hmac.ComputeHash(Encoding.UTF8.GetBytes(jsonState));
            var computedB64 = Convert.ToBase64String(computed);
            var receivedB64 = Convert.ToBase64String(signature);
            throw new CryptographicException($"Invalid state signature. Received: {receivedB64}, Computed: {computedB64}. JsonState: {jsonState}");
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