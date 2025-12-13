using MCPify.Core.Auth.DeviceCode;
using MCPify.Core.Auth.OAuth;
using MCPify.Tests.Integration;

namespace MCPify.Tests;

public class DeviceCodeAuthenticationTests : IAsyncLifetime
{
    private readonly TestOAuthServer _oauthServer = new();

    public async Task InitializeAsync() => await _oauthServer.StartAsync();

    public async Task DisposeAsync() => await _oauthServer.DisposeAsync();

    [Fact]
    public async Task ApplyAsync_PerformDeviceFlow_WhenNoToken()
    {
        var store = new InMemoryTokenStore();
        var accessor = new MockMcpContextAccessor();
        string? promptedUrl = null;
        string? promptedCode = null;

        var auth = new DeviceCodeAuthentication(
            "client_id",
            _oauthServer.DeviceCodeEndpoint,
            _oauthServer.TokenEndpoint,
            "scope",
            store,
            accessor,
            (url, code) =>
            {
                promptedUrl = url;
                promptedCode = code;
                // Simulate user authorizing the device immediately.
                _oauthServer.AuthorizeDevice();
                return Task.CompletedTask;
            },
            _oauthServer.CreateClient()
        );

        var request = new HttpRequestMessage(HttpMethod.Get, "http://api.com");

        await auth.ApplyAsync(request);

        Assert.Equal(_oauthServer.VerificationEndpoint, promptedUrl);
        Assert.NotNull(promptedCode);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);

        var saved = await store.GetTokenAsync("test-session", "DeviceCode");
        Assert.NotNull(saved);
        Assert.Equal(saved!.AccessToken, request.Headers.Authorization?.Parameter);
    }
}