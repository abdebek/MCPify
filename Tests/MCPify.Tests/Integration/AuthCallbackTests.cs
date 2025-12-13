using System.Net;
using MCPify.Core;
using MCPify.Core.Auth;
using MCPify.Core.Auth.OAuth;
using MCPify.Sample.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;

namespace MCPify.Tests.Integration;

public class AuthCallbackTests
{
    [Fact]
    public async Task MapAuthCallback_ExposesDebugInfo_InDevelopment()
    {
        using var host = await CreateHostAsync("Development");
        var client = host.GetTestClient();

        var response = await client.GetAsync("/callback?code=abc&state=invalid.state.format");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();

        // Assert that the content contains exception details (debug info)
        Assert.Contains("Auth exchange failed:", content);
        Assert.Contains("inval...ormat", content);
    }

    [Fact]
    public async Task MapAuthCallback_HidesDebugInfo_InProduction()
    {
        using var host = await CreateHostAsync("Production");
        var client = host.GetTestClient();

        var response = await client.GetAsync("/callback?code=abc&state=invalid.state.format");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();

        // Assert that the content does NOT contain exception details
        Assert.DoesNotContain("invalid.state.format", content);
        Assert.Equal("Auth exchange failed. Please check server logs.", content);
    }

    private async Task<IHost> CreateHostAsync(string environment)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = environment
        });

        builder.WebHost.UseTestServer();

        // Register necessary services for OAuthAuthorizationCodeAuthentication
        var mockTokenStore = new Mock<ISecureTokenStore>();
        var mockAccessor = new Mock<IMcpContextAccessor>();

        builder.Services.AddSingleton(mockTokenStore.Object);
        builder.Services.AddSingleton(mockAccessor.Object);
        builder.Services.AddSingleton<OAuthAuthorizationCodeAuthentication>(sp => 
            new OAuthAuthorizationCodeAuthentication(
                clientId: "test-client",
                authorizationEndpoint: "http://localhost/auth",
                tokenEndpoint: "http://localhost/token",
                scope: "scope",
                secureTokenStore: sp.GetRequiredService<ISecureTokenStore>(),
                mcpContextAccessor: sp.GetRequiredService<IMcpContextAccessor>()
            ));

        var app = builder.Build();

        app.MapAuthCallback("/callback");

        await app.StartAsync();
        return app;
    }
}
