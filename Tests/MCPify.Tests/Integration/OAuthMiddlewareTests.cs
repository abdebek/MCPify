using System.Net;
using MCPify.Core;
using MCPify.Core.Auth;
using MCPify.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MCPify.Tests.Integration;

public class OAuthMiddlewareTests
{
    [Fact]
    public async Task Request_Returns401_WhenNoToken_And_OAuthConfigured()
    {
        using var host = await CreateHostAsync(services =>
        {
            var store = services.GetRequiredService<OAuthConfigurationStore>();
            store.AddConfiguration(new OAuth2Configuration { AuthorizationUrl = "https://auth" });
        });
        
        var client = host.GetTestClient();

        var response = await client.GetAsync("/mcp");
        
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("WWW-Authenticate", response.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value)).Keys);
        var authHeader = response.Headers.WwwAuthenticate.ToString();
        Assert.Contains("resource_metadata_url", authHeader);
    }

    [Fact]
    public async Task Request_Returns200_WhenTokenPresent()
    {
        using var host = await CreateHostAsync(services =>
        {
            var store = services.GetRequiredService<OAuthConfigurationStore>();
            store.AddConfiguration(new OAuth2Configuration { AuthorizationUrl = "https://auth" });
        });
        
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "token");

        var response = await client.GetAsync("/mcp");
        
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Request_Returns200_WhenNoOAuthConfigured()
    {
        using var host = await CreateHostAsync();
        var client = host.GetTestClient();

        var response = await client.GetAsync("/mcp");
        
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<IHost> CreateHostAsync(Action<IServiceProvider>? configure = null)
    {
        return await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddMcpify(options => { });
                        services.AddLogging();
                    })
                    .Configure(app =>
                    {
                        configure?.Invoke(app.ApplicationServices);
                        app.UseMcpifyOAuth();
                        app.Map("/mcp", b => b.Run(async c => 
                        {
                            c.Response.StatusCode = 200;
                            await c.Response.WriteAsync("OK");
                        }));
                    });
            })
            .StartAsync();
    }
}
