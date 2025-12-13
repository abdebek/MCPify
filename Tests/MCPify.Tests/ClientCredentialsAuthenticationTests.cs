using MCPify.Core.Auth.OAuth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;

namespace MCPify.Tests;

public class ClientCredentialsAuthenticationTests
{
    [Fact]
    public async Task AppliesTokenFromTokenEndpoint()
    {
        await using var server = new TestClientCredentialsServer();
        await server.StartAsync();

        var store = new InMemoryTokenStore();
        var accessor = new MockMcpContextAccessor();
        var auth = new ClientCredentialsAuthentication(
            "client-id",
            "client-secret",
            server.TokenEndpoint,
            "scopeA scopeB",
            store,
            accessor,
            server.CreateClient());

        var request = new HttpRequestMessage(HttpMethod.Get, "http://api.com");
        await auth.ApplyAsync(request);

        Assert.NotNull(request.Headers.Authorization);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("cc_token", request.Headers.Authorization.Parameter);
    }

    private sealed class TestClientCredentialsServer : IAsyncDisposable
    {
        private readonly IHost _host;
        public string BaseUrl { get; }
        public string TokenEndpoint => $"{BaseUrl}/token";

        public TestClientCredentialsServer()
        {
            var port = GetRandomUnusedPort();
            BaseUrl = $"http://localhost:{port}";
            _host = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(builder =>
                {
                    builder.UseUrls(BaseUrl);
                    builder.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapPost("/token", async context =>
                            {
                                await context.Response.WriteAsJsonAsync(new
                                {
                                    access_token = "cc_token",
                                    token_type = "Bearer",
                                    expires_in = 3600
                                });
                            });
                        });
                    });
                })
                .Build();
        }

        public async Task StartAsync() => await _host.StartAsync();

        public HttpClient CreateClient() => new() { BaseAddress = new Uri(BaseUrl) };

        private static int GetRandomUnusedPort()
        {
            using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public async ValueTask DisposeAsync()
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }
}
