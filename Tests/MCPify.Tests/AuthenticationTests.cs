using MCPify.Core.Auth;

namespace MCPify.Tests;

public class AuthenticationTests
{
    [Fact]
    public async Task ApiKeyAuthentication_Header_AppliesCorrectly()
    {
        // Arrange
        var auth = new ApiKeyAuthentication("X-API-Key", "secret-value", ApiKeyLocation.Header);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com");

        // Act
        await auth.ApplyAsync(request);

        // Assert
        Assert.True(request.Headers.Contains("X-API-Key"));
        Assert.Equal("secret-value", request.Headers.GetValues("X-API-Key").First());
    }

    [Fact]
    public async Task ApiKeyAuthentication_Query_AppliesCorrectly()
    {
        // Arrange
        var auth = new ApiKeyAuthentication("api_key", "12345", ApiKeyLocation.Query);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api/resource");

        // Act
        await auth.ApplyAsync(request);

        // Assert
        Assert.Equal("http://example.com/api/resource?api_key=12345", request.RequestUri?.ToString());
    }

    [Fact]
    public async Task ApiKeyAuthentication_Query_AppendsToExistingQuery()
    {
        // Arrange
        var auth = new ApiKeyAuthentication("key", "val", ApiKeyLocation.Query);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api?foo=bar");

        // Act
        await auth.ApplyAsync(request);

        // Assert
        var uri = request.RequestUri?.ToString();
        Assert.Contains("foo=bar", uri);
        Assert.Contains("key=val", uri);
    }

    [Fact]
    public async Task BearerAuthentication_AppliesCorrectly()
    {
        // Arrange
        var token = "xyz-token";
        var auth = new BearerAuthentication(token);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com");

        // Act
        await auth.ApplyAsync(request);

        // Assert
        Assert.NotNull(request.Headers.Authorization);
        Assert.Equal("Bearer", request.Headers.Authorization.Scheme);
        Assert.Equal(token, request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task BasicAuthentication_AppliesCorrectly()
    {
        // Arrange
        var username = "user";
        var password = "password";
        var auth = new BasicAuthentication(username, password);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com");

        // Act
        await auth.ApplyAsync(request);

        // Assert
        Assert.NotNull(request.Headers.Authorization);
        Assert.Equal("Basic", request.Headers.Authorization.Scheme);

        var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization.Parameter!));
        Assert.Equal($"{username}:{password}", decoded);
    }
}