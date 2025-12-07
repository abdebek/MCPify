using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Web;

namespace MCPify.Core.Auth.OAuth;

public class OAuthAuthorizationCodeAuthentication : IAuthenticationProvider
{
    private readonly string _clientId;
    private readonly string? _clientSecret;
    private readonly string _authorizationEndpoint;
    private readonly string _tokenEndpoint;
    private readonly string _scope;
    private readonly ITokenStore _tokenStore;
    private readonly HttpClient _httpClient;
    private readonly string _callbackPath;
    private readonly string? _redirectUri;
    private readonly string _callbackHost;
    private readonly Action<string>? _openBrowserAction;

    public OAuthAuthorizationCodeAuthentication(
        string clientId,
        string authorizationEndpoint,
        string tokenEndpoint,
        string scope,
        ITokenStore tokenStore,
        string? clientSecret = null,
        HttpClient? httpClient = null,
        string callbackPath = "/callback",
        string? redirectUri = null,
        string callbackHost = "localhost",
        Action<string>? openBrowserAction = null)
    {
        _clientId = clientId;
        _authorizationEndpoint = authorizationEndpoint;
        _tokenEndpoint = tokenEndpoint;
        _scope = scope;
        _tokenStore = tokenStore;
        _clientSecret = clientSecret;
        _httpClient = httpClient ?? new HttpClient();
        _callbackPath = callbackPath;
        _redirectUri = redirectUri;
        _callbackHost = callbackHost;
        _openBrowserAction = openBrowserAction;
    }

    public async Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        var tokenData = await _tokenStore.GetTokenAsync(cancellationToken);

        if (tokenData != null && (!tokenData.ExpiresAt.HasValue || tokenData.ExpiresAt.Value > DateTimeOffset.UtcNow.AddMinutes(1)))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenData.AccessToken);
            return;
        }

        if (tokenData?.RefreshToken != null)
        {
            try
            {
                tokenData = await RefreshTokenAsync(tokenData.RefreshToken, cancellationToken);
                await _tokenStore.SaveTokenAsync(tokenData, cancellationToken);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenData.AccessToken);
                return;
            }
            catch
            {
                // Refresh failed, fall back to full login
            }
        }

        // Full Login
        tokenData = await PerformLoginAsync(cancellationToken);
        await _tokenStore.SaveTokenAsync(tokenData, cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenData.AccessToken);
    }

    private async Task<TokenData> PerformLoginAsync(CancellationToken cancellationToken)
    {
        try
        {
            string redirectUri;
            if (!string.IsNullOrEmpty(_redirectUri))
            {
                redirectUri = _redirectUri;
            }
            else
            {
                var port = GetRandomUnusedPort();
                redirectUri = $"http://{_callbackHost}:{port}{_callbackPath}";
            }

            using var listener = new HttpListener();
            listener.Prefixes.Add(redirectUri.EndsWith("/") ? redirectUri : redirectUri + "/");
            listener.Start();

            var state = Guid.NewGuid().ToString();

            var query = HttpUtility.ParseQueryString("");
            query["response_type"] = "code";
            query["client_id"] = _clientId;
            query["redirect_uri"] = redirectUri;
            query["scope"] = _scope;
            query["state"] = state;
            
            var authUrl = $"{_authorizationEndpoint}?{query}";
            
            if (_openBrowserAction != null)
            {
                _openBrowserAction(authUrl);
            }
            else
            {
                OpenBrowser(authUrl);
            }

            try
            {
                var contextTask = listener.GetContextAsync();
                // Simple cancellation support
                using var reg = cancellationToken.Register(() => listener.Stop());
                
                var context = await contextTask;
                
                var req = context.Request;
                var res = context.Response;

                var returnedState = req.QueryString["state"];
                var code = req.QueryString["code"];
                var error = req.QueryString["error"];

                if (!string.IsNullOrEmpty(error))
                {
                    await SendResponseAsync(res, $"<html><body><h1>Login Failed</h1><p>{WebUtility.HtmlEncode(error)}</p></body></html>", cancellationToken);
                    throw new Exception($"OAuth Error: {error}");
                }

                if (returnedState != state || string.IsNullOrEmpty(code))
                {
                    await SendResponseAsync(res, "<html><body><h1>Login Failed</h1><p>Invalid state or missing code.</p></body></html>", cancellationToken);
                    throw new Exception("Invalid state or missing code.");
                }

                await SendResponseAsync(res, "<html><body><h1>Login Successful</h1><p>You can close this window and return to the application.</p><script>window.close();</script></body></html>", cancellationToken);

                return await ExchangeCodeForTokenAsync(code, redirectUri, cancellationToken);
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                throw new TaskCanceledException();
            }
        }
        catch (Exception ex)
        {
            File.AppendAllText("oauth_error.log", $"{DateTime.Now}: {ex}\n");
            throw;
        }
    }

    private async Task SendResponseAsync(HttpListenerResponse response, string content, CancellationToken token)
    {
        var buffer = Encoding.UTF8.GetBytes(content);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer, token);
        response.OutputStream.Close();
    }

    private async Task<TokenData> ExchangeCodeForTokenAsync(string code, string redirectUri, CancellationToken cancellationToken)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "grant_type", "authorization_code" },
            { "client_id", _clientId },
            { "code", code },
            { "redirect_uri", redirectUri },
            { "client_secret", _clientSecret ?? "" }
        });

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

    private async Task<TokenData> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "grant_type", "refresh_token" },
            { "client_id", _clientId },
            { "refresh_token", refreshToken },
            { "client_secret", _clientSecret ?? "" }
        });

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

    private int GetRandomUnusedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
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
}
