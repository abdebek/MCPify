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
    private readonly string? _resourceUrl; // RFC 8707 resource parameter
    private readonly string _providerName;
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
        bool usePkce = true,
        Action<string>? authorizationUrlEmitter = null,
        string? stateSecret = null,
        bool allowDefaultSessionFallback = false,
        string? resourceUrl = null,
        string? providerName = null)
    {
        _clientId = clientId;
        _authorizationEndpoint = authorizationEndpoint;
        _tokenEndpoint = tokenEndpoint;
        _scope = scope;
        _secureTokenStore = secureTokenStore;
        _mcpContextAccessor = mcpContextAccessor;
        _clientSecret = clientSecret;
        _httpClient = httpClient ?? HttpClientFallback.Create(nameof(OAuthAuthorizationCodeAuthentication));
        _redirectUri = redirectUri;
        _openBrowserAction = openBrowserAction;
        _usePkce = usePkce;
        _authorizationUrlEmitter = authorizationUrlEmitter;
        var resolvedStateSecret = stateSecret ?? Environment.GetEnvironmentVariable("MCPIFY_STATE_SECRET");
        if (string.IsNullOrWhiteSpace(resolvedStateSecret) || resolvedStateSecret.Length < 32)
        {
            throw new ArgumentException(
                "stateSecret is required for OAuth state signing and must be at least 32 characters. " +
                "Pass it via the stateSecret parameter or set the MCPIFY_STATE_SECRET environment variable.",
                nameof(stateSecret));
        }
        _stateSecret = resolvedStateSecret;
        _allowDefaultSessionFallback = allowDefaultSessionFallback;
        _resourceUrl = resourceUrl;
        _providerName = providerName ?? "OAuth";
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
        var sessionId = _mcpContextAccessor.SessionId;

        if (string.IsNullOrEmpty(sessionId))
        {
            throw new InvalidOperationException("SessionId not set in MCP context. Cannot apply authentication.");
        }

        foreach (var lookupKey in ResolveTokenLookupKeys(sessionId))
        {
            var tokenData = await _secureTokenStore.GetTokenAsync(lookupKey, _providerName, cancellationToken);

            if (tokenData != null && (!tokenData.ExpiresAt.HasValue || tokenData.ExpiresAt.Value > DateTimeOffset.UtcNow.AddMinutes(1)))
            {
                if (!string.Equals(lookupKey, sessionId, StringComparison.Ordinal))
                {
                    await _secureTokenStore.SaveTokenAsync(sessionId, _providerName, tokenData, cancellationToken);
                }

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenData.AccessToken);
                return;
            }

            if (tokenData?.RefreshToken != null)
            {
                try
                {
                    tokenData = await RefreshTokenAsync(tokenData.RefreshToken, lookupKey, cancellationToken);
                    await _secureTokenStore.SaveTokenAsync(lookupKey, _providerName, tokenData, cancellationToken);
                    if (!string.Equals(lookupKey, sessionId, StringComparison.Ordinal))
                    {
                        await _secureTokenStore.SaveTokenAsync(sessionId, _providerName, tokenData, cancellationToken);
                    }
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenData.AccessToken);
                    return;
                }
                catch (Exception)
                {
                    await _secureTokenStore.DeleteTokenAsync(lookupKey, _providerName, cancellationToken);
                }
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

        string? codeVerifier = null;
        if (_usePkce)
        {
            // PKCE verifier was stored under the session HANDLE (Temp ID)
            var pkceTokenData = await _secureTokenStore.GetTokenAsync(sessionHandle, _pkceStorePrefix + nonce, cancellationToken)
                ?? throw new InvalidOperationException($"PKCE verifier not found for session '{sessionHandle}' and nonce '{nonce}'. Login process invalid or expired.");
            codeVerifier = pkceTokenData.AccessToken;
        }

        var tokenData = await ExchangeCodeForTokenAsync(code, redirectUri, codeVerifier, cancellationToken);

        // Always store under the session handle. We do not rekey on unvalidated id_token sub
        // (the id_token signature is not validated here, so sub is not trusted as a storage key).
        _mcpContextAccessor.SessionId = sessionHandle;
        await _secureTokenStore.SaveTokenAsync(sessionHandle, _providerName, tokenData, cancellationToken);

        if (_allowDefaultSessionFallback && !string.Equals(sessionHandle, Constants.DefaultSessionId, StringComparison.Ordinal))
        {
            await _secureTokenStore.SaveTokenAsync(Constants.DefaultSessionId, _providerName, tokenData, cancellationToken);
        }

        if (_usePkce)
        {
            await _secureTokenStore.DeleteTokenAsync(sessionHandle, _pkceStorePrefix + nonce, cancellationToken);
        }

        return tokenData;
    }

    private IEnumerable<string> ResolveTokenLookupKeys(string sessionId)
    {
        var keys = new List<string>();
        AddLookupKey(keys, sessionId);

        if (_allowDefaultSessionFallback)
        {
            AddLookupKey(keys, Constants.DefaultSessionId);
        }

        return keys;
    }

    private static void AddLookupKey(List<string> keys, string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (!keys.Contains(key, StringComparer.Ordinal))
        {
            keys.Add(key);
        }
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
            ProviderName = _providerName
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
            throw new CryptographicException("Invalid state signature. The OAuth state may have been tampered with or the signing key has changed.");
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
