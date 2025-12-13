using System.Net;
using System.Net.Http.Json;
using MCPify.Core;
using MCPify.Core.Auth;
using MCPify.Hosting;
using MCPify.OpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;

namespace MCPify.Tests.Integration;

public class OAuthMetadataEndpointTests
{
    [Fact]
    public async Task GetMetadata_Returns404_WhenNoOAuthConfigured()
    {
        using var host = await CreateHostAsync();
        var client = host.GetTestClient();

        var response = await client.GetAsync("/.well-known/oauth-protected-resource");
        
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMetadata_ReturnsMetadata_WhenOAuthConfigured()
    {
        var authUrl = "https://auth.example.com/authorize";
        var tokenUrl = "https://auth.example.com/token";

        using var host = await CreateHostAsync(services =>
        {
            var store = services.GetRequiredService<OAuthConfigurationStore>();
            store.AddConfiguration(new OAuth2Configuration
            {
                AuthorizationUrl = authUrl,
                TokenUrl = tokenUrl,
                Scopes = new Dictionary<string, string> { { "scope1", "desc" } }
            });
        });
        
        var client = host.GetTestClient();

        var response = await client.GetAsync("/.well-known/oauth-protected-resource");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var metadata = await response.Content.ReadFromJsonAsync<ProtectedResourceMetadata>();
        Assert.NotNull(metadata);
        Assert.Contains("https://auth.example.com", metadata!.AuthorizationServers);
        Assert.Contains("scope1", metadata.ScopesSupported);
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
                        services.AddRouting();
                    })
                    .Configure(app =>
                    {
                        configure?.Invoke(app.ApplicationServices);
                        app.UseRouting();
                        app.UseEndpoints(endpoints => 
                        {
                            endpoints.MapMcpifyEndpoint();
                        });
                    });
            })
            .StartAsync();
    }

    private class ProtectedResourceMetadata
    {
        [System.Text.Json.Serialization.JsonPropertyName("resource")]
        public string Resource { get; set; } = default!;

        [System.Text.Json.Serialization.JsonPropertyName("authorization_servers")]
        public List<string> AuthorizationServers { get; set; } = default!;

        [System.Text.Json.Serialization.JsonPropertyName("scopes_supported")]
        public List<string> ScopesSupported { get; set; } = default!;
    }
}