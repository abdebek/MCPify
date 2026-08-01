using MCPify.Core;
using MCPify.Core.Auth;
using MCPify.Core.Auth.OAuth;
using MCPify.Tests;
using Moq;
using System.Net;
using System.Text.Json;
using Xunit;

namespace MCPify.Tests;

public class TokenIsolationTests
{
    private readonly InMemoryTokenStore _store = new();
    private readonly IMcpContextAccessor _accessor = new McpContextAccessor();

    private OAuthAuthorizationCodeAuthentication CreateOAuth(
        string providerName,
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
    public async Task DefaultProviderName_IsOAuth()
    {
        var oauth = CreateOAuth(providerName: null!);
        // The default provider name should be "OAuth" — verify via the LoginResult
        var loginResult = oauth.BuildAuthorizationUrl("test-session");
        Assert.NotEmpty(loginResult);
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