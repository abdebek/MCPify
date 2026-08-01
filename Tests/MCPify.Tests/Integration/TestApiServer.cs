using System.Net;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace MCPify.Tests.Integration;

public sealed class TestApiServer : IAsyncDisposable
{
    private readonly IHost _host;

    public string BaseUrl { get; }

    public TestApiServer()
    {
        var port = GetRandomUnusedPort();
        BaseUrl = $"http://localhost:{port}";

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls(BaseUrl);

        var app = builder.Build();
        ConfigureApp(app);
        _host = app;
    }

    public async Task StartAsync() => await _host.StartAsync();

    public HttpClient CreateClient() => new() { BaseAddress = new Uri(BaseUrl) };

    private void ConfigureApp(WebApplication app)
    {
        app.MapGet("/users/{id:int}", async context =>
        {
            var id = int.Parse(context.Request.RouteValues["id"]?.ToString() ?? "0");
            await context.Response.WriteAsJsonAsync(new
            {
                id,
                path = context.Request.Path.Value,
                query = context.Request.QueryString.Value
            });
        });

        app.MapPost("/echo", async context =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync();
            await context.Response.WriteAsync(body);
        });

        app.MapGet("/auth-check", async context =>
        {
            var auth = context.Request.Headers.Authorization.ToString();
            await context.Response.WriteAsJsonAsync(new { authorization = auth });
        });

        app.MapGet("/apikey-check", async context =>
        {
            var apiKey = context.Request.Headers["X-API-Key"].ToString();
            await context.Response.WriteAsJsonAsync(new { apiKey });
        });
    }

    private static int GetRandomUnusedPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}
