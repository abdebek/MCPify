using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;
using MCPify.Core.Auth.DeviceCode;
using MCPify.Core.Auth.OAuth;

namespace MCPify.Tests.Integration;

public class OAuthE2ETests : IAsyncLifetime
{
    private readonly TestOAuthServer _provider = new();

    public async Task InitializeAsync() => await _provider.StartAsync();

    public async Task DisposeAsync() => await _provider.DisposeAsync();

    [Fact]
    public async Task AuthorizationCodeFlow_EndToEnd()
    {
        var tokenStore = new InMemoryTokenStore();
        var accessor = new MockMcpContextAccessor();

        var auth = new OAuthAuthorizationCodeAuthentication(
            "client_id",
            _provider.AuthorizationEndpoint,
            _provider.TokenEndpoint,
            "scope",
            tokenStore,
            accessor,
            httpClient: _provider.CreateClient(),
            redirectUri: "http://localhost/callback"
        );

        // 1. Initiate Flow
        var authUrl = auth.BuildAuthorizationUrl(accessor.SessionId!);

        // 2. Simulate User visiting Auth URL
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        var browser = new HttpClient(handler);
        var authResponse = await browser.GetAsync(authUrl);

        Assert.Equal(HttpStatusCode.Redirect, authResponse.StatusCode);
        var callbackUrl = authResponse.Headers.Location!;

        // 3. Extract Code and State from Callback URL
        var query = QueryHelpers.ParseQuery(callbackUrl.Query);
        var code = query["code"].ToString();
        var state = query["state"].ToString();

        // 4. Handle Callback
        await auth.HandleAuthorizationCallbackAsync(code, state);

        // 5. Apply Async (Now it should have the token)
        var request = new HttpRequestMessage(HttpMethod.Get, "http://api.com");
        await auth.ApplyAsync(request);

        Assert.NotNull(request.Headers.Authorization);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.False(string.IsNullOrEmpty(request.Headers.Authorization.Parameter));

        var stored = await tokenStore.GetTokenAsync("test-session", "OAuth");
        Assert.NotNull(stored);
        Assert.Equal(request.Headers.Authorization.Parameter, stored!.AccessToken);
    }

    [Fact]
    public async Task DeviceCodeFlow_EndToEnd()
    {
        var tokenStore = new InMemoryTokenStore();
        var accessor = new MockMcpContextAccessor();

        Func<string, string, Task> userPrompt = (uri, code) =>
        {
            _provider.AuthorizeDevice();
            return Task.CompletedTask;
        };

        var auth = new DeviceCodeAuthentication(
            "client_id",
            _provider.DeviceCodeEndpoint,
            _provider.TokenEndpoint,
            "scope",
            tokenStore,
            accessor,
            userPrompt,
            _provider.CreateClient()
        );

        var request = new HttpRequestMessage(HttpMethod.Get, "http://api.com");

        await auth.ApplyAsync(request);

        Assert.NotNull(request.Headers.Authorization);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.False(string.IsNullOrEmpty(request.Headers.Authorization!.Parameter));
    }

    [Fact]
    public async Task ApplyAsync_UsesPassThroughToken_WhenPresentInContext()
    {
        var tokenStore = new InMemoryTokenStore();
        var accessor = new MockMcpContextAccessor { AccessToken = "pass-through-token" };

        var auth = new OAuthAuthorizationCodeAuthentication(
            "client_id",
            _provider.AuthorizationEndpoint,
            _provider.TokenEndpoint,
            "scope",
            tokenStore,
            accessor,
            httpClient: _provider.CreateClient(),
            redirectUri: "http://localhost/callback"
        );

        var request = new HttpRequestMessage(HttpMethod.Get, "http://api.com");

        await auth.ApplyAsync(request);

        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("pass-through-token", request.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task OpenIdConnect_Metadata_And_IdToken_Validation()
    {
        var client = _provider.CreateClient();

        var config = JsonDocument.Parse(await client.GetStringAsync($"{_provider.BaseUrl}/.well-known/openid-configuration")).RootElement;
        Assert.Equal(_provider.AuthorizationEndpoint, config.GetProperty("authorization_endpoint").GetString());
        Assert.Equal(_provider.TokenEndpoint, config.GetProperty("token_endpoint").GetString());
        Assert.Equal(_provider.JwksEndpoint, config.GetProperty("jwks_uri").GetString());

        var redirectUri = "http://localhost/callback";
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        var browser = new HttpClient(handler);
        var authResponse = await browser.GetAsync($"{_provider.AuthorizationEndpoint}?response_type=code&client_id=client_id&redirect_uri={WebUtility.UrlEncode(redirectUri)}&state=xyz&scope=openid%20profile");
        Assert.Equal(HttpStatusCode.Redirect, authResponse.StatusCode);

        var location = authResponse.Headers.Location!;
        var query = QueryHelpers.ParseQuery(location.Query);
        var code = query["code"].ToString();
        Assert.Equal("xyz", query["state"].ToString());
        Assert.False(string.IsNullOrEmpty(code));

        var tokenResponse = await client.PostAsync(_provider.TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "grant_type", "authorization_code" },
            { "code", code },
            { "redirect_uri", redirectUri },
            { "client_id", "client_id" }
        }));
        tokenResponse.EnsureSuccessStatusCode();

        var json = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync()).RootElement;
        var idToken = json.GetProperty("id_token").GetString();
        Assert.False(string.IsNullOrEmpty(idToken));

        var handlerJwt = new JwtSecurityTokenHandler();
        handlerJwt.InboundClaimTypeMap.Clear();
        var principal = handlerJwt.ValidateToken(idToken!, new TokenValidationParameters
        {
            ValidIssuer = _provider.Issuer,
            ValidAudience = _provider.Audience,
            IssuerSigningKey = _provider.SigningKey,
            ValidateLifetime = true,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true
        }, out _);

        var subject = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                      ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                      ?? principal.FindFirst(ClaimTypes.Name)?.Value;

        Assert.Equal("test-user", subject);
    }
}