using MCPify.Core.Auth.OAuth;
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
        await store.SaveTokenAsync("test-session", "OAuth", new TokenData("valid_token", "refresh_token", DateTimeOffset.UtcNow.AddMinutes(10)));

        var auth = new OAuthAuthorizationCodeAuthentication(
            "client_id",
            _oauthServer.AuthorizationEndpoint,
            _oauthServer.TokenEndpoint,
            "scope",
            store,
            accessor,
            httpClient: _oauthServer.CreateClient(),
            redirectUri: "http://localhost/callback");

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
        await store.SaveTokenAsync("test-session", "OAuth", new TokenData("expired_token", "refresh_token", DateTimeOffset.UtcNow.AddMinutes(-10)));

        var auth = new OAuthAuthorizationCodeAuthentication(
            "client_id",
            _oauthServer.AuthorizationEndpoint,
            _oauthServer.TokenEndpoint,
            "scope",
            store,
            accessor,
            httpClient: _oauthServer.CreateClient(),
            redirectUri: "http://localhost/callback");

        var request = new HttpRequestMessage(HttpMethod.Get, "http://api.com");

        await auth.ApplyAsync(request);

        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.NotEqual("expired_token", request.Headers.Authorization?.Parameter);

        var saved = await store.GetTokenAsync("test-session", "OAuth");
        Assert.NotNull(saved);
        Assert.Equal(request.Headers.Authorization?.Parameter, saved!.AccessToken);
    }
}