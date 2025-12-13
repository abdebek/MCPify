using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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

    [Fact]
    public async Task ApplyAsync_ReusesExistingValidToken()
    {
        var store = new InMemoryTokenStore();
        var accessor = new MockMcpContextAccessor();
        await store.SaveTokenAsync("test-session", "ClientCredentials", new TokenData("cached-token", null, DateTimeOffset.UtcNow.AddMinutes(5)));

        var handler = new FailOnSendHandler();
        var auth = new ClientCredentialsAuthentication(
            "client-id",
            "client-secret",
            "https://auth.example.com/token",
            "scope",
            store,
            accessor,
            new HttpClient(handler));

        var request = new HttpRequestMessage(HttpMethod.Get, "http://api.com");
        await auth.ApplyAsync(request);

        Assert.NotNull(request.Headers.Authorization);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("cached-token", request.Headers.Authorization.Parameter);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ApplyAsync_RefreshesToken_WhenExpiringSoon()
    {
        var store = new InMemoryTokenStore();
        var accessor = new MockMcpContextAccessor();
        await store.SaveTokenAsync("test-session", "ClientCredentials", new TokenData("expiring-token", null, DateTimeOffset.UtcNow.AddSeconds(30)));

        var handler = new StubTokenHandler("new-cc-token");
        var auth = new ClientCredentialsAuthentication(
            "client-id",
            "client-secret",
            "https://auth.example.com/token",
            "scopeA scopeB",
            store,
            accessor,
            new HttpClient(handler));

        var request = new HttpRequestMessage(HttpMethod.Get, "http://api.com");
        await auth.ApplyAsync(request);

        Assert.NotNull(request.Headers.Authorization);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("new-cc-token", request.Headers.Authorization.Parameter);
        Assert.Equal(1, handler.CallCount);

        var saved = await store.GetTokenAsync("test-session", "ClientCredentials");
        Assert.NotNull(saved);
        Assert.Equal("new-cc-token", saved!.AccessToken);
        Assert.True(saved.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(50));
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

    private sealed class FailOnSendHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException("Token endpoint should not be called for cached tokens.");
        }
    }

    private sealed class StubTokenHandler : HttpMessageHandler
    {
        private readonly string _accessToken;

        public int CallCount { get; private set; }

        public StubTokenHandler(string accessToken)
        {
            _accessToken = accessToken;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("https://auth.example.com/token", request.RequestUri!.ToString());

            var body = JsonSerializer.Serialize(new
            {
                access_token = _accessToken,
                token_type = "Bearer",
                expires_in = 3600
            });

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
