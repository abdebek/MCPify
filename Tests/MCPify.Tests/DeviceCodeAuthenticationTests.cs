using MCPify.Core.Auth.DeviceCode;
using MCPify.Core.Auth.OAuth;
using Moq;
using RichardSzalay.MockHttp;
using System.Net;
using System.Text.Json;

namespace MCPify.Tests;

public class DeviceCodeAuthenticationTests
{
    private readonly MockHttpMessageHandler _mockHttp;
    private readonly HttpClient _httpClient;
    private readonly Mock<ITokenStore> _mockStore;

    public DeviceCodeAuthenticationTests()
    {
        _mockHttp = new MockHttpMessageHandler();
        _httpClient = _mockHttp.ToHttpClient();
        _mockStore = new Mock<ITokenStore>();
    }

    [Fact]
    public async Task ApplyAsync_PerformDeviceFlow_WhenNoToken()
    {
        // Arrange
        _mockStore.Setup(s => s.GetTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((TokenData?)null);

        _mockHttp.Expect(HttpMethod.Post, "http://device")
            .Respond("application/json", JsonSerializer.Serialize(new
            {
                device_code = "dc_123",
                user_code = "UC-123",
                verification_uri = "http://verify",
                expires_in = 600,
                interval = 0 // Immediate for test
            }));

        _mockHttp.Expect(HttpMethod.Post, "http://token")
            .WithFormData("grant_type", "urn:ietf:params:oauth:grant-type:device_code")
            .WithFormData("device_code", "dc_123")
            .Respond("application/json", JsonSerializer.Serialize(new
            {
                access_token = "new_token",
                expires_in = 3600,
                refresh_token = "rt_123"
            }));

        string? capturedUrl = null;
        string? capturedCode = null;

        var auth = new DeviceCodeAuthentication(
            "client_id", 
            "http://device", 
            "http://token", 
            "scope", 
            _mockStore.Object, 
            (url, code) => 
            {
                capturedUrl = url;
                capturedCode = code;
                return Task.CompletedTask;
            }, 
            _httpClient
        );

        var request = new HttpRequestMessage(HttpMethod.Get, "http://api.com");

        // Act
        await auth.ApplyAsync(request);

        // Assert
        Assert.Equal("http://verify", capturedUrl);
        Assert.Equal("UC-123", capturedCode);
        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("new_token", request.Headers.Authorization?.Parameter);

        _mockStore.Verify(s => s.SaveTokenAsync(It.IsAny<TokenData>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
