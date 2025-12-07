using MCPify.Core.Auth.DeviceCode;
using MCPify.Core.Auth.OAuth;
using Moq;

namespace MCPify.Tests.Integration;

public class OAuthE2ETests : IClassFixture<MockOAuthProvider>
{
    private readonly MockOAuthProvider _provider;

    public OAuthE2ETests()
    {
        _provider = new MockOAuthProvider();
    }

    [Fact]
    public async Task AuthorizationCodeFlow_EndToEnd()
    {
        // 1. Start the Mock Provider
        await _provider.StartAsync();

        // 2. Setup Client
        var tokenStore = new InMemoryTokenStore();
        var httpClient = new HttpClient();

        // This simulates the user's browser hitting the URL that the authenticator "opened"
        Action<string> openBrowserSimulation = (url) =>
        {
            // Run in background to simulate browser separate process
            Task.Run(async () =>
            {
                // Give the listener a moment to start
                await Task.Delay(500);

                // The URL is the Provider's /authorize endpoint.
                // In a real browser, this would redirect back to localhost.
                // We manually follow that redirect logic here or just hit the authorize endpoint

                // Fetch the Authorize URL -> Provider Redirects to -> Localhost Callback
                var handler = new HttpClientHandler { AllowAutoRedirect = true };
                var browserClient = new HttpClient(handler);

                await browserClient.GetAsync(url);
            });
        };

        var auth = new OAuthAuthorizationCodeAuthentication(
            "client_id",
            _provider.AuthorizationEndpoint,
            _provider.TokenEndpoint,
            "scope",
            tokenStore,
            null,
            httpClient,
            openBrowserAction: openBrowserSimulation
        );

        var request = new HttpRequestMessage(HttpMethod.Get, "http://api.com");

        // 3. Act - Trigger Auth
        // This will: Start Listener -> Call OpenBrowser -> Browser hits Provider -> Provider redirects -> Listener gets code -> Swaps for token
        await auth.ApplyAsync(request);

        // 4. Assert
        Assert.NotNull(request.Headers.Authorization);
        Assert.Equal("Bearer", request.Headers.Authorization.Scheme);
        Assert.Equal("access_token_auth_code", request.Headers.Authorization.Parameter);

        // Verify token was stored
        var stored = await tokenStore.GetTokenAsync();
        Assert.NotNull(stored);
        Assert.Equal("access_token_auth_code", stored.AccessToken);
    }

    [Fact]
    public async Task DeviceCodeFlow_EndToEnd()
    {
        // 1. Start Mock Provider
        await _provider.StartAsync();

        // 2. Setup Client
        var tokenStore = new InMemoryTokenStore();
        var httpClient = new HttpClient();

        // Simulate user action: When prompted, they "click the link" (authorize on provider)
        Func<string, string, Task> userPrompt = async (uri, code) =>
        {
            // Verify correct code passed
            Assert.Equal("UC-1234", code);

            // Simulate user authorizing on the provider side
            // In reality, user visits `uri` and types `code`.
            // Here, we just tell our Mock Provider to flip the "Authorized" bit.
            _provider.AuthorizeDevice();

            await Task.CompletedTask;
        };

        var auth = new DeviceCodeAuthentication(
            "client_id",
            _provider.DeviceCodeEndpoint,
            _provider.TokenEndpoint,
            "scope",
            tokenStore,
            userPrompt,
            httpClient
        );

        var request = new HttpRequestMessage(HttpMethod.Get, "http://api.com");

        // 3. Act
        await auth.ApplyAsync(request);

        // 4. Assert
        Assert.NotNull(request.Headers.Authorization);
        Assert.Equal("Bearer", request.Headers.Authorization.Scheme);
        Assert.Equal("access_token_device_flow", request.Headers.Authorization.Parameter);
    }
}
