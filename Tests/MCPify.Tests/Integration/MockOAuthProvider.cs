using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace MCPify.Tests.Integration;

public class MockOAuthProvider : IAsyncDisposable
{
    private readonly IHost _host;
    public string BaseUrl { get; private set; }
    public string TokenEndpoint => $"{BaseUrl}/token";
    public string AuthorizationEndpoint => $"{BaseUrl}/authorize";
    public string DeviceCodeEndpoint => $"{BaseUrl}/device/code";

    // State for verification
    public string? LastCode { get; private set; }
    public string? LastDeviceCode { get; private set; }
    public bool DeviceAuthorized { get; private set; }

    public MockOAuthProvider()
    {
        var port = GetRandomUnusedPort();
        BaseUrl = $"http://localhost:{port}";

        _host = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseUrls(BaseUrl);
                webBuilder.Configure(ConfigureApp);
            })
            .Build();
    }

    public async Task StartAsync()
    {
        await _host.StartAsync();
    }

    public void AuthorizeDevice()
    {
        DeviceAuthorized = true;
    }

    private void ConfigureApp(IApplicationBuilder app)
    {
        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            // Authorization Endpoint (Browser redirects here)
            endpoints.MapGet("/authorize", async context =>
            {
                var redirectUri = context.Request.Query["redirect_uri"];
                var state = context.Request.Query["state"];
                var code = "auth_code_" + Guid.NewGuid();
                LastCode = code;

                // Simulate redirect back to client
                context.Response.Redirect($"{redirectUri}?code={code}&state={state}");
                await Task.CompletedTask;
            });

            // Token Endpoint
            endpoints.MapPost("/token", async context =>
            {
                var form = await context.Request.ReadFormAsync();
                var grantType = form["grant_type"];

                if (grantType == "authorization_code")
                {
                    if (form["code"] != LastCode)
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsync("Invalid code");
                        return;
                    }

                    await WriteTokenResponse(context, "access_token_auth_code");
                }
                else if (grantType == "urn:ietf:params:oauth:grant-type:device_code")
                {
                    if (!DeviceAuthorized)
                    {
                        context.Response.StatusCode = 400;
                        await context.Response.WriteAsync("{\"error\": \"authorization_pending\"}");
                        return;
                    }

                    await WriteTokenResponse(context, "access_token_device_flow");
                }
                else if (grantType == "refresh_token")
                {
                    await WriteTokenResponse(context, "access_token_refreshed");
                }
            });

            // Device Code Endpoint
            endpoints.MapPost("/device/code", async context =>
            {
                var deviceCode = "dc_" + Guid.NewGuid();
                LastDeviceCode = deviceCode;
                DeviceAuthorized = false; // Reset

                await context.Response.WriteAsJsonAsync(new
                {
                    device_code = deviceCode,
                    user_code = "UC-1234",
                    verification_uri = $"{BaseUrl}/device",
                    expires_in = 300,
                    interval = 1
                });
            });
        });
    }

    private async Task WriteTokenResponse(HttpContext context, string accessToken)
    {
        await context.Response.WriteAsJsonAsync(new
        {
            access_token = accessToken,
            token_type = "Bearer",
            expires_in = 3600,
            refresh_token = "refresh_token_mock"
        });
    }

    private int GetRandomUnusedPort()
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
