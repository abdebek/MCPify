using MCPify.Core.Auth.OAuth;
using MCPify.Core;
using MCPify.Tests.Integration;

namespace MCPify.Tests;

public class OAuthAuthenticationTests : IAsyncLifetime
{
    private readonly TestOAuthServer _oauthServer = new();

    public async Task InitializeAsync() => await _oauthServer.StartAsync();

    public async Task DisposeAsync() => await _oauthServer.DisposeAsync();

    [Fact]
    public async Task ApplyAsync_UsesExistingValidToken()
    {
        var store = new InMemoryTokenStore();
        var accessor = new MockMcpContextAccessor();

        var auth = new OAuthAuthorizationCodeAuthentication(
            "client_id",
            _oauthServer.AuthorizationEndpoint,
            _oauthServer.TokenEndpoint,
            "scope",
            store,
            accessor,
            httpClient: _oauthServer.CreateClient(),
            redirectUri: "http://localhost/callback",
            stateSecret: "test-state-secret-key-for-hmac-signing-32+");

        await store.SaveTokenAsync("test-session", auth.ProviderName, new TokenData("valid_token", "refresh_token", DateTimeOffset.UtcNow.AddMinutes(10)));

        var request = new HttpRequestMessage(HttpMethod.Get, "http://api.com");

        await auth.ApplyAsync(request);

        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("valid_token", request.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task ApplyAsync_RefreshesExpiredToken()
    {
        var store = new InMemoryTokenStore();
        var accessor = new MockMcpContextAccessor();

        var auth = new OAuthAuthorizationCodeAuthentication(
            "client_id",
            _oauthServer.AuthorizationEndpoint,
            _oauthServer.TokenEndpoint,
            "scope",
            store,
            accessor,
            httpClient: _oauthServer.CreateClient(),
            redirectUri: "http://localhost/callback",
            stateSecret: "test-state-secret-key-for-hmac-signing-32+");

        await store.SaveTokenAsync("test-session", auth.ProviderName, new TokenData("expired_token", "refresh_token", DateTimeOffset.UtcNow.AddMinutes(-10)));

        var request = new HttpRequestMessage(HttpMethod.Get, "http://api.com");

        await auth.ApplyAsync(request);

        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.NotEqual("expired_token", request.Headers.Authorization?.Parameter);

        var saved = await store.GetTokenAsync("test-session", auth.ProviderName);
        Assert.NotNull(saved);
        Assert.Equal(request.Headers.Authorization?.Parameter, saved!.AccessToken);
    }

    [Fact]
    public async Task ApplyAsync_UsesDefaultSessionToken_WhenFallbackEnabled()
    {
        var store = new InMemoryTokenStore();
        var accessor = new MockMcpContextAccessor { SessionId = "session-a" };

        var auth = new OAuthAuthorizationCodeAuthentication(
            "client_id",
            _oauthServer.AuthorizationEndpoint,
            _oauthServer.TokenEndpoint,
            "scope",
            store,
            accessor,
            httpClient: _oauthServer.CreateClient(),
            redirectUri: "http://localhost/callback",
            allowDefaultSessionFallback: true,
            stateSecret: "test-state-secret-key-for-hmac-signing-32+");

        await store.SaveTokenAsync(Constants.DefaultSessionId, auth.ProviderName, new TokenData("default_token", "refresh_token", DateTimeOffset.UtcNow.AddMinutes(10)));

        var request = new HttpRequestMessage(HttpMethod.Get, "http://api.com");

        await auth.ApplyAsync(request);

        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("default_token", request.Headers.Authorization?.Parameter);

        var sessionToken = await store.GetTokenAsync("session-a", auth.ProviderName);
        Assert.NotNull(sessionToken);
        Assert.Equal("default_token", sessionToken!.AccessToken);
    }

    [Fact]
    public async Task ApplyAsync_DoesNotUseDefaultSessionToken_WhenFallbackDisabled()
    {
        var store = new InMemoryTokenStore();
        var accessor = new MockMcpContextAccessor { SessionId = "session-b" };

        var auth = new OAuthAuthorizationCodeAuthentication(
            "client_id",
            _oauthServer.AuthorizationEndpoint,
            _oauthServer.TokenEndpoint,
            "scope",
            store,
            accessor,
            httpClient: _oauthServer.CreateClient(),
            redirectUri: "http://localhost/callback",
            allowDefaultSessionFallback: false,
            stateSecret: "test-state-secret-key-for-hmac-signing-32+");

        await store.SaveTokenAsync(Constants.DefaultSessionId, auth.ProviderName, new TokenData("default_token", "refresh_token", DateTimeOffset.UtcNow.AddMinutes(10)));

        var request = new HttpRequestMessage(HttpMethod.Get, "http://api.com");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => auth.ApplyAsync(request));
        Assert.Contains("Run the login tool", exception.Message);
    }

    [Fact]
    public void Constructor_ThrowsWhenStateSecretIsNull()
    {
        Assert.Throws<ArgumentException>(() =>
            new OAuthAuthorizationCodeAuthentication(
                "client_id",
                _oauthServer.AuthorizationEndpoint,
                _oauthServer.TokenEndpoint,
                "scope",
                new InMemoryTokenStore(),
                new MockMcpContextAccessor(),
                redirectUri: "http://localhost/callback",
                stateSecret: null));
    }

    [Fact]
    public void Constructor_ThrowsWhenStateSecretIsEmpty()
    {
        Assert.Throws<ArgumentException>(() =>
            new OAuthAuthorizationCodeAuthentication(
                "client_id",
                _oauthServer.AuthorizationEndpoint,
                _oauthServer.TokenEndpoint,
                "scope",
                new InMemoryTokenStore(),
                new MockMcpContextAccessor(),
                redirectUri: "http://localhost/callback",
                stateSecret: ""));
    }

    [Fact]
    public void Constructor_ThrowsWhenStateSecretIsTooShort()
    {
        Assert.Throws<ArgumentException>(() =>
            new OAuthAuthorizationCodeAuthentication(
                "client_id",
                _oauthServer.AuthorizationEndpoint,
                _oauthServer.TokenEndpoint,
                "scope",
                new InMemoryTokenStore(),
                new MockMcpContextAccessor(),
                redirectUri: "http://localhost/callback",
                stateSecret: "short"));
    }

    [Fact]
    public void Constructor_AcceptsStateSecretFromEnvironmentVariable()
    {
        var envVar = "MCPIFY_STATE_SECRET";
        var previous = Environment.GetEnvironmentVariable(envVar);
        try
        {
            Environment.SetEnvironmentVariable(envVar, "env-provided-state-secret-key-32+chars");
            var auth = new OAuthAuthorizationCodeAuthentication(
                "client_id",
                _oauthServer.AuthorizationEndpoint,
                _oauthServer.TokenEndpoint,
                "scope",
                new InMemoryTokenStore(),
                new MockMcpContextAccessor(),
                redirectUri: "http://localhost/callback");
            Assert.NotNull(auth);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, previous);
        }
    }
}
