using MCPify.Core.Auth;
using MCPify.Core.Auth.OAuth;
using MCPify.Core.Auth.DeviceCode;
using System.Web;

namespace MCPify.Tests.Unit;

public class ResourceParameterTests
{
    private readonly InMemoryTokenStore _tokenStore = new();
    private readonly MockMcpContextAccessor _contextAccessor = new();

    #region OAuthAuthorizationCodeAuthentication Tests

    [Fact]
    public void BuildAuthorizationUrl_IncludesResourceParameter_WhenConfigured()
    {
        var resourceUrl = "https://api.example.com";
        var auth = new OAuthAuthorizationCodeAuthentication(
            clientId: "client-id",
            authorizationEndpoint: "https://auth.example.com/authorize",
            tokenEndpoint: "https://auth.example.com/token",
            scope: "read write",
            secureTokenStore: _tokenStore,
            mcpContextAccessor: _contextAccessor,
            redirectUri: "https://app.example.com/callback",
            resourceUrl: resourceUrl
        );

        _contextAccessor.SessionId = "test-session";

        var url = auth.BuildAuthorizationUrl("test-session");
        var uri = new Uri(url);
        var query = HttpUtility.ParseQueryString(uri.Query);

        Assert.Equal(resourceUrl, query["resource"]);
    }

    [Fact]
    public void BuildAuthorizationUrl_OmitsResourceParameter_WhenNotConfigured()
    {
        var auth = new OAuthAuthorizationCodeAuthentication(
            clientId: "client-id",
            authorizationEndpoint: "https://auth.example.com/authorize",
            tokenEndpoint: "https://auth.example.com/token",
            scope: "read write",
            secureTokenStore: _tokenStore,
            mcpContextAccessor: _contextAccessor,
            redirectUri: "https://app.example.com/callback"
        );

        _contextAccessor.SessionId = "test-session";

        var url = auth.BuildAuthorizationUrl("test-session");
        var uri = new Uri(url);
        var query = HttpUtility.ParseQueryString(uri.Query);

        Assert.Null(query["resource"]);
    }

    [Fact]
    public void BuildAuthorizationUrl_IncludesAllRequiredParameters()
    {
        var auth = new OAuthAuthorizationCodeAuthentication(
            clientId: "my-client",
            authorizationEndpoint: "https://auth.example.com/authorize",
            tokenEndpoint: "https://auth.example.com/token",
            scope: "openid profile",
            secureTokenStore: _tokenStore,
            mcpContextAccessor: _contextAccessor,
            redirectUri: "https://app.example.com/callback",
            usePkce: true,
            resourceUrl: "https://api.example.com"
        );

        _contextAccessor.SessionId = "test-session";

        var url = auth.BuildAuthorizationUrl("test-session");
        var uri = new Uri(url);
        var query = HttpUtility.ParseQueryString(uri.Query);

        Assert.Equal("code", query["response_type"]);
        Assert.Equal("my-client", query["client_id"]);
        Assert.Equal("https://app.example.com/callback", query["redirect_uri"]);
        Assert.Equal("openid profile", query["scope"]);
        Assert.NotNull(query["state"]);
        Assert.NotNull(query["code_challenge"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("https://api.example.com", query["resource"]);
    }

    #endregion

    #region ClientCredentialsAuthentication Tests

    [Fact]
    public async Task ClientCredentials_IncludesResourceParameter_WhenConfigured()
    {
        var resourceUrl = "https://api.example.com";
        var capturedContent = (FormUrlEncodedContent?)null;

        var mockHandler = new MockHttpMessageHandler(request =>
        {
            capturedContent = request.Content as FormUrlEncodedContent;
            return Task.FromResult(new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent("{\"access_token\":\"token\",\"expires_in\":3600}")
            });
        });

        var httpClient = new HttpClient(mockHandler);

        var auth = new ClientCredentialsAuthentication(
            clientId: "client-id",
            clientSecret: "client-secret",
            tokenEndpoint: "https://auth.example.com/token",
            scope: "read write",
            secureTokenStore: _tokenStore,
            mcpContextAccessor: _contextAccessor,
            httpClient: httpClient,
            resourceUrl: resourceUrl
        );

        _contextAccessor.SessionId = "test-session";

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
        await auth.ApplyAsync(request);

        Assert.NotNull(capturedContent);
        var formData = await capturedContent.ReadAsStringAsync();
        Assert.Contains($"resource={Uri.EscapeDataString(resourceUrl)}", formData);
    }

    [Fact]
    public async Task ClientCredentials_OmitsResourceParameter_WhenNotConfigured()
    {
        var capturedContent = (FormUrlEncodedContent?)null;

        var mockHandler = new MockHttpMessageHandler(request =>
        {
            capturedContent = request.Content as FormUrlEncodedContent;
            return Task.FromResult(new HttpResponseMessage
            {
                StatusCode = System.Net.HttpStatusCode.OK,
                Content = new StringContent("{\"access_token\":\"token\",\"expires_in\":3600}")
            });
        });

        var httpClient = new HttpClient(mockHandler);

        var auth = new ClientCredentialsAuthentication(
            clientId: "client-id",
            clientSecret: "client-secret",
            tokenEndpoint: "https://auth.example.com/token",
            scope: "read write",
            secureTokenStore: _tokenStore,
            mcpContextAccessor: _contextAccessor,
            httpClient: httpClient
        );

        _contextAccessor.SessionId = "test-session";

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
        await auth.ApplyAsync(request);

        Assert.NotNull(capturedContent);
        var formData = await capturedContent.ReadAsStringAsync();
        Assert.DoesNotContain("resource=", formData);
    }

    #endregion

    #region DeviceCodeAuthentication Tests

    [Fact]
    public async Task DeviceCode_IncludesResourceParameter_InDeviceCodeRequest()
    {
        var resourceUrl = "https://api.example.com";
        var requestCount = 0;
        var capturedDeviceCodeContent = (FormUrlEncodedContent?)null;

        var mockHandler = new MockHttpMessageHandler(request =>
        {
            requestCount++;
            if (requestCount == 1)
            {
                // Device code request
                capturedDeviceCodeContent = request.Content as FormUrlEncodedContent;
                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = System.Net.HttpStatusCode.OK,
                    Content = new StringContent("{\"device_code\":\"dc\",\"user_code\":\"UC\",\"verification_uri\":\"https://auth.example.com/device\",\"expires_in\":600,\"interval\":1}")
                });
            }
            else
            {
                // Token polling request
                return Task.FromResult(new HttpResponseMessage
                {
                    StatusCode = System.Net.HttpStatusCode.OK,
                    Content = new StringContent("{\"access_token\":\"token\",\"refresh_token\":\"rt\",\"expires_in\":3600}")
                });
            }
        });

        var httpClient = new HttpClient(mockHandler);
        var userPromptCalled = false;

        var auth = new DeviceCodeAuthentication(
            clientId: "client-id",
            deviceCodeEndpoint: "https://auth.example.com/device/code",
            tokenEndpoint: "https://auth.example.com/token",
            scope: "read write",
            secureTokenStore: _tokenStore,
            mcpContextAccessor: _contextAccessor,
            userPrompt: (_, _) => { userPromptCalled = true; return Task.CompletedTask; },
            httpClient: httpClient,
            resourceUrl: resourceUrl
        );

        _contextAccessor.SessionId = "test-session";

        var request = new HttpRequestMessage(HttpMethod.Get, "https://api.example.com/data");
        await auth.ApplyAsync(request);

        Assert.True(userPromptCalled);
        Assert.NotNull(capturedDeviceCodeContent);
        var formData = await capturedDeviceCodeContent.ReadAsStringAsync();
        Assert.Contains($"resource={Uri.EscapeDataString(resourceUrl)}", formData);
    }

    #endregion

    #region Helper Classes

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }

    #endregion
}
