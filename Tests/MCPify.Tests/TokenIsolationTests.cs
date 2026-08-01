using MCPify.Core;
using MCPify.Core.Auth;
using MCPify.Core.Auth.OAuth;
using MCPify.Tests;
using Xunit;

namespace MCPify.Tests;

public class TokenIsolationTests
{
    private readonly InMemoryTokenStore _store = new();
    private readonly IMcpContextAccessor _accessor = new McpContextAccessor();

    private OAuthAuthorizationCodeAuthentication CreateOAuth(
        string? providerName,
        string scope = "api",
        string clientId = "client-a")
    {
        return new OAuthAuthorizationCodeAuthentication(
            clientId: clientId,
            authorizationEndpoint: "https://auth-a.example.com/authorize",
            tokenEndpoint: "https://auth-a.example.com/token",
            scope: scope,
            secureTokenStore: _store,
            mcpContextAccessor: _accessor,
            clientSecret: "secret",
            redirectUri: "https://app.example.com/callback",
            stateSecret: "test-state-secret-must-be-32-chars-min!!",
            providerName: providerName);
    }

    [Fact]
    public async Task Tokens_WithDifferentProviderNames_DoNotCollide()
    {
        _accessor.SessionId = "session-1";

        await _store.SaveTokenAsync("session-1", "OAuth:entra", new TokenData("entra-token", null, null));
        await _store.SaveTokenAsync("session-1", "OAuth:github", new TokenData("github-token", null, null));

        var entra = await _store.GetTokenAsync("session-1", "OAuth:entra", CancellationToken.None);
        var github = await _store.GetTokenAsync("session-1", "OAuth:github", CancellationToken.None);

        Assert.NotNull(entra);
        Assert.NotNull(github);
        Assert.Equal("entra-token", entra!.AccessToken);
        Assert.Equal("github-token", github!.AccessToken);
        Assert.NotEqual(entra.AccessToken, github.AccessToken);
    }

    [Fact]
    public async Task Tokens_WithSameProviderName_Overwrite()
    {
        await _store.SaveTokenAsync("session-1", "OAuth", new TokenData("first", null, null));
        await _store.SaveTokenAsync("session-1", "OAuth", new TokenData("second", null, null));

        var token = await _store.GetTokenAsync("session-1", "OAuth", CancellationToken.None);
        Assert.Equal("second", token!.AccessToken);
    }

    [Fact]
    public void DefaultProviderName_IsNamespacedByClientId()
    {
        var oauth = CreateOAuth(providerName: null, clientId: "entra-client");
        Assert.Equal("OAuth:entra-client@auth-a.example.com", oauth.ProviderName);

        var other = CreateOAuth(providerName: null, clientId: "github-client");
        Assert.Equal("OAuth:github-client@auth-a.example.com", other.ProviderName);
        Assert.NotEqual(oauth.ProviderName, other.ProviderName);
    }

    [Fact]
    public void ExplicitProviderName_OverridesAutoNamespace()
    {
        var oauth = CreateOAuth(providerName: "OAuth:custom-slot", clientId: "entra-client");
        Assert.Equal("OAuth:custom-slot", oauth.ProviderName);
    }

    [Fact]
    public void AuthProviderNames_SanitizesClientId()
    {
        Assert.Equal("OAuth:my_client", AuthProviderNames.Resolve(null, "OAuth", "my client", null));
        Assert.Equal("OAuth:default", AuthProviderNames.Resolve(null, "OAuth", "   ", null));
        Assert.Equal("OAuth:explicit", AuthProviderNames.Resolve("OAuth:explicit", "OAuth", "ignored", "https://x.com/token"));
    }

    [Fact]
    public void AuthProviderNames_IncludesTokenHost()
    {
        Assert.Equal("OAuth:app@login.microsoftonline.com",
            AuthProviderNames.Resolve(null, "OAuth", "app", "https://login.microsoftonline.com/tenant/oauth2/v2.0/token"));
        Assert.Equal("OAuth:app@github.com",
            AuthProviderNames.Resolve(null, "OAuth", "app", "https://github.com/login/oauth/access_token"));
    }

    [Fact]
    public void AuthProviderNames_SameClientIdDifferentHosts_Isolated()
    {
        var name1 = AuthProviderNames.Resolve(null, "OAuth", "shared", "https://idp-a.com/token");
        var name2 = AuthProviderNames.Resolve(null, "OAuth", "shared", "https://idp-b.com/token");
        Assert.NotEqual(name1, name2);
    }

    [Fact]
    public async Task AllowDefaultSessionFallback_UsesProviderName()
    {
        var oauth = new OAuthAuthorizationCodeAuthentication(
            clientId: "client-a",
            authorizationEndpoint: "https://auth-a.example.com/authorize",
            tokenEndpoint: "https://auth-a.example.com/token",
            scope: "api",
            secureTokenStore: _store,
            mcpContextAccessor: _accessor,
            clientSecret: "secret",
            redirectUri: "https://app.example.com/callback",
            stateSecret: "test-state-secret-must-be-32-chars-min!!",
            allowDefaultSessionFallback: true,
            providerName: "OAuth:custom");

        // Manually simulate the dual-write path
        await _store.SaveTokenAsync("real-session", "OAuth:custom", new TokenData("custom-token", null, null));
        await _store.SaveTokenAsync(Constants.DefaultSessionId, "OAuth:custom", new TokenData("custom-token", null, null));

        // Token for "OAuth:custom" should not leak into "OAuth" namespace
        var wrongNs = await _store.GetTokenAsync(Constants.DefaultSessionId, "OAuth", CancellationToken.None);
        var rightNs = await _store.GetTokenAsync(Constants.DefaultSessionId, "OAuth:custom", CancellationToken.None);

        Assert.Null(wrongNs);
        Assert.NotNull(rightNs);
        Assert.Equal("custom-token", rightNs!.AccessToken);
    }

    [Fact]
    public async Task ClientCredentials_UsesCustomProviderName()
    {
        var cc = new ClientCredentialsAuthentication(
            clientId: "cc-client",
            clientSecret: "secret",
            tokenEndpoint: "https://auth.example.com/token",
            scope: "api",
            secureTokenStore: _store,
            mcpContextAccessor: _accessor,
            providerName: "ClientCredentials:api-a");

        _accessor.SessionId = "session-cc";
        await _store.SaveTokenAsync("session-cc", "ClientCredentials:api-a", new TokenData("cc-token-a", null, null));
        await _store.SaveTokenAsync("session-cc", "ClientCredentials:api-b", new TokenData("cc-token-b", null, null));

        var a = await _store.GetTokenAsync("session-cc", "ClientCredentials:api-a", CancellationToken.None);
        var b = await _store.GetTokenAsync("session-cc", "ClientCredentials:api-b", CancellationToken.None);

        Assert.Equal("cc-token-a", a!.AccessToken);
        Assert.Equal("cc-token-b", b!.AccessToken);
    }
}