using System.Net;
using System.Text;
using System.Text.Json;
using MCPify.Core;
using MCPify.Core.Auth;
using MCPify.Core.Auth.OAuth;
using MCPify.Core.Auth.TokenExchange;

namespace MCPify.Tests;

public class TokenExchangeAuthenticationTests
{
    private readonly InMemoryTokenStore _tokenStore = new();
    private readonly MockMcpContextAccessor _contextAccessor = new();

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        return new HttpClient(new StubTokenHandler(handler))
        {
            BaseAddress = new Uri("https://auth.example.com")
        };
    }

    private TokenExchangeAuthentication CreateAuth(
        HttpClient httpClient,
        string providerName = "TokenExchange",
        string? scope = null,
        string? resource = null,
        string? audience = null)
    {
        return new TokenExchangeAuthentication(
            tokenEndpoint: "https://auth.example.com/token",
            clientId: "my-client",
            clientSecret: "my-secret",
            scope: scope,
            resource: resource,
            audience: audience,
            tokenStore: _tokenStore,
            mcpContextAccessor: _contextAccessor,
            httpClient: httpClient,
            providerName: providerName);
    }

    [Fact]
    public async Task ApplyAsync_SetsAuthorizationHeader_WhenExchangeSucceeds()
    {
        _contextAccessor.AccessToken = "mcp-access-token";
        _contextAccessor.SessionId = "session-1";

        var httpClient = CreateHttpClient(_ => CreateTokenResponse("upstream-token-123", 3600));
        var auth = CreateAuth(httpClient);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
        await auth.ApplyAsync(request);

        Assert.NotNull(request.Headers.Authorization);
        Assert.Equal("Bearer", request.Headers.Authorization.Scheme);
        Assert.Equal("upstream-token-123", request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task ApplyAsync_DoesNotSetHeader_WhenNoMcpToken()
    {
        _contextAccessor.AccessToken = null;
        _contextAccessor.SessionId = "session-1";

        var exchangeCalled = false;
        var httpClient = CreateHttpClient(_ =>
        {
            exchangeCalled = true;
            return CreateTokenResponse("should-not-reach", 3600);
        });
        var auth = CreateAuth(httpClient);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
        await auth.ApplyAsync(request);

        Assert.Null(request.Headers.Authorization);
        Assert.False(exchangeCalled);
    }

    [Fact]
    public async Task ApplyAsync_StripsBearer_FromSubjectToken()
    {
        _contextAccessor.AccessToken = "Bearer my-token-with-prefix";
        _contextAccessor.SessionId = "session-1";

        string? capturedSubjectToken = null;
        var httpClient = CreateHttpClient(req =>
        {
            var body = req.Content!.ReadAsStringAsync().Result;
            var pairs = body.Split('&').ToDictionary(
                p => Uri.UnescapeDataString(p.Split('=')[0]),
                p => Uri.UnescapeDataString(p.Split('=')[1]));
            capturedSubjectToken = pairs["subject_token"];
            return CreateTokenResponse("upstream-token", 3600);
        });
        var auth = CreateAuth(httpClient);

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
        await auth.ApplyAsync(request);

        Assert.Equal("my-token-with-prefix", capturedSubjectToken);
    }

    [Fact]
    public async Task ExchangeTokenAsync_SendsCorrectGrantType()
    {
        string? capturedGrantType = null;
        var httpClient = CreateHttpClient(req =>
        {
            var body = req.Content!.ReadAsStringAsync().Result;
            var pairs = body.Split('&').ToDictionary(
                p => Uri.UnescapeDataString(p.Split('=')[0]),
                p => Uri.UnescapeDataString(p.Split('=')[1]));
            capturedGrantType = pairs["grant_type"];
            return CreateTokenResponse("token", 3600);
        });
        var auth = CreateAuth(httpClient);

        await auth.ExchangeTokenAsync("subject-token", CancellationToken.None);

        Assert.Equal("urn:ietf:params:oauth:grant-type:token-exchange", capturedGrantType);
    }

    [Fact]
    public async Task ExchangeTokenAsync_SendsSubjectTokenType()
    {
        string? capturedType = null;
        var httpClient = CreateHttpClient(req =>
        {
            var body = req.Content!.ReadAsStringAsync().Result;
            var pairs = body.Split('&').ToDictionary(
                p => Uri.UnescapeDataString(p.Split('=')[0]),
                p => Uri.UnescapeDataString(p.Split('=')[1]));
            capturedType = pairs["subject_token_type"];
            return CreateTokenResponse("token", 3600);
        });
        var auth = CreateAuth(httpClient);

        await auth.ExchangeTokenAsync("subject-token", CancellationToken.None);

        Assert.Equal("urn:ietf:params:oauth:token-type:access_token", capturedType);
    }

    [Fact]
    public async Task ExchangeTokenAsync_IncludesOptionalParameters()
    {
        Dictionary<string, string>? capturedParams = null;
        var httpClient = CreateHttpClient(req =>
        {
            var body = req.Content!.ReadAsStringAsync().Result;
            capturedParams = body.Split('&').ToDictionary(
                p => Uri.UnescapeDataString(p.Split('=')[0]),
                p => Uri.UnescapeDataString(p.Split('=')[1]));
            return CreateTokenResponse("token", 3600);
        });
        var auth = CreateAuth(httpClient, scope: "api.read", resource: "https://api.example.com", audience: "api-service");

        await auth.ExchangeTokenAsync("subject-token", CancellationToken.None);

        Assert.NotNull(capturedParams);
        Assert.Equal("api.read", capturedParams["scope"]);
        Assert.Equal("https://api.example.com", capturedParams["resource"]);
        Assert.Equal("api-service", capturedParams["audience"]);
        Assert.Equal("my-client", capturedParams["client_id"]);
        Assert.Equal("my-secret", capturedParams["client_secret"]);
    }

    [Fact]
    public async Task ExchangeTokenAsync_Throws_OnHttpError()
    {
        var httpClient = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\":\"invalid_request\"}")
        });
        var auth = CreateAuth(httpClient);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            auth.ExchangeTokenAsync("bad-token", CancellationToken.None));
        Assert.Contains("Token exchange failed", ex.Message);
    }

    [Fact]
    public async Task ExchangeTokenAsync_Throws_OnMissingAccessToken()
    {
        var httpClient = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"token_type\":\"bearer\"}")
        });
        var auth = CreateAuth(httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            auth.ExchangeTokenAsync("subject-token", CancellationToken.None));
    }

    [Fact]
    public async Task GetOrExchangeTokenAsync_CachesToken()
    {
        _contextAccessor.AccessToken = "mcp-token";
        _contextAccessor.SessionId = "session-1";

        var callCount = 0;
        var httpClient = CreateHttpClient(_ =>
        {
            callCount++;
            return CreateTokenResponse("upstream-token", 3600);
        });
        var auth = CreateAuth(httpClient);

        // First call - should exchange
        var token1 = await auth.GetOrExchangeTokenAsync("session-1", CancellationToken.None);
        Assert.Equal("upstream-token", token1);
        Assert.Equal(1, callCount);

        // Second call - should use cache
        var token2 = await auth.GetOrExchangeTokenAsync("session-1", CancellationToken.None);
        Assert.Equal("upstream-token", token2);
        Assert.Equal(1, callCount); // Not called again
    }

    [Fact]
    public async Task GetOrExchangeTokenAsync_ReExchanges_WhenTokenExpired()
    {
        _contextAccessor.AccessToken = "mcp-token";
        _contextAccessor.SessionId = "session-1";

        // Pre-seed an expired token
        var expired = new TokenData("old-token", null, DateTimeOffset.UtcNow.AddSeconds(-10));
        await _tokenStore.SaveTokenAsync("session-1", "TokenExchange", expired);

        var httpClient = CreateHttpClient(_ => CreateTokenResponse("fresh-token", 3600));
        var auth = CreateAuth(httpClient);

        var token = await auth.GetOrExchangeTokenAsync("session-1", CancellationToken.None);

        Assert.Equal("fresh-token", token);
    }

    [Fact]
    public async Task GetOrExchangeTokenAsync_ReExchanges_WhenTokenNearExpiry()
    {
        _contextAccessor.AccessToken = "mcp-token";
        _contextAccessor.SessionId = "session-1";

        // Pre-seed a token that expires within 30 seconds
        var nearExpiry = new TokenData("almost-expired", null, DateTimeOffset.UtcNow.AddSeconds(20));
        await _tokenStore.SaveTokenAsync("session-1", "TokenExchange", nearExpiry);

        var httpClient = CreateHttpClient(_ => CreateTokenResponse("fresh-token", 3600));
        var auth = CreateAuth(httpClient);

        var token = await auth.GetOrExchangeTokenAsync("session-1", CancellationToken.None);

        Assert.Equal("fresh-token", token);
    }

    [Fact]
    public async Task GetOrExchangeTokenAsync_UsesCustomProviderName()
    {
        _contextAccessor.AccessToken = "mcp-token";
        _contextAccessor.SessionId = "session-1";

        var httpClient = CreateHttpClient(_ => CreateTokenResponse("custom-token", 3600));
        var auth = CreateAuth(httpClient, providerName: "MyCustomExchange");

        await auth.GetOrExchangeTokenAsync("session-1", CancellationToken.None);

        // Verify it was cached under the custom provider name
        var cached = await _tokenStore.GetTokenAsync("session-1", "MyCustomExchange");
        Assert.NotNull(cached);
        Assert.Equal("custom-token", cached.AccessToken);

        // Should NOT be cached under default name
        var defaultCached = await _tokenStore.GetTokenAsync("session-1", "TokenExchange");
        Assert.Null(defaultCached);
    }

    [Fact]
    public async Task ExchangeTokenAsync_ParsesRefreshToken()
    {
        var json = JsonSerializer.Serialize(new
        {
            access_token = "new-access",
            refresh_token = "new-refresh",
            expires_in = 1800,
            token_type = "Bearer"
        });
        var httpClient = CreateHttpClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        var auth = CreateAuth(httpClient);

        var result = await auth.ExchangeTokenAsync("subject-token", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("new-access", result.AccessToken);
        Assert.Equal("new-refresh", result.RefreshToken);
        Assert.NotNull(result.ExpiresAt);
    }

    [Fact]
    public void Constructor_ThrowsOnNullTokenEndpoint()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new TokenExchangeAuthentication(
                tokenEndpoint: null!,
                clientId: "client",
                clientSecret: null,
                scope: null,
                resource: null,
                audience: null,
                tokenStore: _tokenStore,
                mcpContextAccessor: _contextAccessor));
    }

    [Fact]
    public void Constructor_ThrowsOnNullClientId()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new TokenExchangeAuthentication(
                tokenEndpoint: "https://auth.example.com/token",
                clientId: null!,
                clientSecret: null,
                scope: null,
                resource: null,
                audience: null,
                tokenStore: _tokenStore,
                mcpContextAccessor: _contextAccessor));
    }

    [Fact]
    public void Constructor_ThrowsOnNullTokenStore()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TokenExchangeAuthentication(
                tokenEndpoint: "https://auth.example.com/token",
                clientId: "client",
                clientSecret: null,
                scope: null,
                resource: null,
                audience: null,
                tokenStore: null!,
                mcpContextAccessor: _contextAccessor));
    }

    [Fact]
    public void Constructor_ThrowsOnNullContextAccessor()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new TokenExchangeAuthentication(
                tokenEndpoint: "https://auth.example.com/token",
                clientId: "client",
                clientSecret: null,
                scope: null,
                resource: null,
                audience: null,
                tokenStore: _tokenStore,
                mcpContextAccessor: null!));
    }

    // --- Helpers ---

    private static HttpResponseMessage CreateTokenResponse(string accessToken, int expiresIn)
    {
        var json = JsonSerializer.Serialize(new
        {
            access_token = accessToken,
            token_type = "Bearer",
            expires_in = expiresIn
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private class StubTokenHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubTokenHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
