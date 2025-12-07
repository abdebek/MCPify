using MCPify.Core.Auth.OAuth;
using Moq;
using RichardSzalay.MockHttp;
using System.Net;
using System.Text.Json;

namespace MCPify.Tests;

public class OAuthAuthenticationTests
{
    private readonly MockHttpMessageHandler _mockHttp;
    private readonly HttpClient _httpClient;
    private readonly Mock<ITokenStore> _mockStore;

    public OAuthAuthenticationTests()
    {
        _mockHttp = new MockHttpMessageHandler();
        _httpClient = _mockHttp.ToHttpClient();
        _mockStore = new Mock<ITokenStore>();
    }

    [Fact]
    public async Task ApplyAsync_UsesExistingValidToken()
    {
        // Arrange
        var token = new TokenData("valid_token", "refresh_token", DateTimeOffset.UtcNow.AddMinutes(10));
        _mockStore.Setup(s => s.GetTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(token);

        var auth = new OAuthAuthorizationCodeAuthentication(
            "client_id", "http://auth", "http://token", "scope", _mockStore.Object, null, _httpClient, "/callback");

        var request = new HttpRequestMessage(HttpMethod.Get, "http://api.com");

        // Act
        await auth.ApplyAsync(request);

        // Assert
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("valid_token", request.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task ApplyAsync_RefreshesExpiredToken()
    {
        // Arrange
        var expiredToken = new TokenData("expired_token", "refresh_token", DateTimeOffset.UtcNow.AddMinutes(-10));
        _mockStore.Setup(s => s.GetTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expiredToken);

        _mockHttp.Expect(HttpMethod.Post, "http://token")
            .WithFormData("grant_type", "refresh_token")
            .WithFormData("refresh_token", "refresh_token")
            .Respond("application/json", JsonSerializer.Serialize(new
            {
                access_token = "new_token",
                expires_in = 3600,
                refresh_token = "new_refresh_token"
            }));

        var auth = new OAuthAuthorizationCodeAuthentication(
            "client_id", "http://auth", "http://token", "scope", _mockStore.Object, null, _httpClient, "/callback");

        var request = new HttpRequestMessage(HttpMethod.Get, "http://api.com");

        // Act
        await auth.ApplyAsync(request);

        // Assert
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("new_token", request.Headers.Authorization?.Parameter);
        
        _mockStore.Verify(s => s.SaveTokenAsync(It.Is<TokenData>(t => t.AccessToken == "new_token"), It.IsAny<CancellationToken>()), Times.Once);
        _mockHttp.VerifyNoOutstandingExpectation();
    }
}
