using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MCPify.Core;
using MCPify.Core.Auth;
using MCPify.Core.Auth.OAuth;
using MCPify.Hosting;
using MCPify.Sample;
using MCPify.Sample.Auth;
using MCPify.Sample.Data;
using MCPify.Sample.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Playwright;
using Xunit;

namespace MCPify.Tests.Integration;

/// <summary>
/// Full Sample smoke test: boots the real Sample app, runs the OAuth browser flow via Playwright,
/// then exercises MCP tools via JSON-RPC over HTTP. This is the closest automated test to G-1.0.
/// </summary>
[Trait("Category", "Playwright")]
public class SamplePlaywrightSmokeTests : IAsyncLifetime
{
    private WebApplication? _app;
    private string _baseUrl = "";
    private HttpClient _httpClient = null!;
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    public async Task InitializeAsync()
    {
        // Use port 5005 which is one of the Worker's pre-registered redirect URIs
        _baseUrl = "http://localhost:5005";

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = Array.Empty<string>(),
            ApplicationName = typeof(MCPify.Sample.Worker).Assembly.FullName
        });
        builder.WebHost.UseUrls(_baseUrl);

        // Mirror the Sample's Program.cs setup
        // Use a unique in-memory DB name to avoid conflicts with other tests
        builder.Configuration["Demo:BaseUrl"] = _baseUrl;
        builder.Configuration["Demo:StateSecret"] = "test-state-secret-for-playwright-smoke-32+";
        builder.Configuration["Mcpify:Transport"] = "Http";

        builder.Services.AddDemoDatabaseAndAuth(_baseUrl, dbName: $"playwright-{Guid.NewGuid()}");
        builder.Services.AddDemoSwagger(_baseUrl);
        builder.Services.AddDemoMcpify(builder.Configuration, _baseUrl, $"{_baseUrl}/auth/callback");
        builder.Services.AddHostedService<MCPify.Sample.Worker>();
        builder.Services.AddControllers();

        _app = builder.Build();

        _app.UseCors("AllowAll");
        _app.UseSwagger();
        _app.UseSwaggerUI();
        _app.UseDynamicClientRegistration();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapDemoEndpoints("/auth/callback");
        _app.MapMcpifyEndpoint("/mcp");

        await _app.StartAsync();

        // Wait for the Worker hosted service to seed the OpenIddict client
        await WaitForWorkerAsync(timeoutSeconds: 10);

        _httpClient = new HttpClient { BaseAddress = new Uri(_baseUrl) };

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (_browser != null) await _browser.CloseAsync();
        _playwright?.Dispose();
        _httpClient?.Dispose();
        if (_app != null) await _app.StopAsync();
    }

    [Fact]
    public async Task FullSmoke_OAuthLogin_AndMcpToolCall()
    {
        // 1. Get the OAuth authorize URL from the LoginTool
        var authProvider = _app!.Services.GetRequiredService<OAuthAuthorizationCodeAuthentication>();
        var tokenStore = _app.Services.GetRequiredService<ISecureTokenStore>();
        var sessionId = "playwright-test-session";
        var authUrl = authProvider.BuildAuthorizationUrl(sessionId);

        // 2. Use Playwright to navigate the OAuth flow
        // The Sample's OpenIddict authorize endpoint auto-signs in and redirects to /auth/callback
        var page = await _browser.NewPageAsync();

        // Capture console and response events for debugging
        var responses = new List<string>();
        page.Response += (_, response) =>
        {
            responses.Add($"{response.Status} {response.Url}");
        };

        try
        {
            await page.GotoAsync(authUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 15000 });
        }
        catch (PlaywrightException ex)
        {
            // Navigation may throw if the callback page auto-closes via JS
            Assert.True(responses.Any(r => r.Contains("/auth/callback")),
                $"Navigation failed: {ex.Message}. Responses: {string.Join(", ", responses)}");
        }

        var pageContent = await page.ContentAsync();
        var pageUrl = page.Url;

        Assert.True(
            pageUrl.Contains("/auth/callback") ||
            pageContent.Contains("Login Successful", StringComparison.OrdinalIgnoreCase),
            $"Expected callback page. URL: {pageUrl}, Content: {pageContent[..Math.Min(500, pageContent.Length)]}. Responses: {string.Join(", ", responses)}");

        await page.CloseAsync();

        // 3. Verify the token was stored
        var tokenData = await tokenStore.GetTokenAsync(sessionId, "OAuth", CancellationToken.None);
        Assert.NotNull(tokenData);
        Assert.False(string.IsNullOrEmpty(tokenData!.AccessToken));

        // 4. Initialize MCP session (required by SDK 2.0 Streamable HTTP)
        var initResponse = await SendMcpRequest("initialize", new
        {
            protocolVersion = "2025-06-18",
            capabilities = new { },
            clientInfo = new { name = "playwright-test", version = "1.0" }
        });
        Assert.True(initResponse.RootElement.TryGetProperty("result", out _),
            $"initialize failed. Response: {initResponse.RootElement.GetRawText()}");

        // Extract session ID from response header and use it for subsequent requests
        _mcpSessionId = _lastMcpSessionId;

        // 5. Call "tools/list" to verify tools are registered
        var listResponse = await SendMcpRequest("tools/list", new { });
        Assert.True(listResponse.RootElement.TryGetProperty("result", out var listResult),
            $"tools/list did not return 'result'. Full response: {listResponse.RootElement.GetRawText()}");
        Assert.True(listResult.TryGetProperty("tools", out var tools));
        var toolNames = tools.EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Contains("login_auth_code_pkce", toolNames);
        Assert.Contains(toolNames, n => n!.StartsWith("api_"));

        // 5. Call an authenticated local tool (api_getsecrets or similar)
        var secretsTool = toolNames.FirstOrDefault(n => n!.Contains("secret", StringComparison.OrdinalIgnoreCase));
        if (secretsTool != null)
        {
            var callResponse = await SendMcpRequest("tools/call", new { name = secretsTool, arguments = new { } });
            Assert.True(callResponse.RootElement.TryGetProperty("result", out var callResult));
            Assert.True(callResult.TryGetProperty("content", out var content));
            var text = content.EnumerateArray()
                .Where(c => c.TryGetProperty("type", out var t) && t.GetString() == "text")
                .Select(c => c.GetProperty("text").GetString())
                .FirstOrDefault();
            Assert.NotNull(text);
        }
    }

    [Fact]
    public async Task FullSmoke_ProtectedResourceMetadata_IsServed()
    {
        var response = await _httpClient.GetAsync("/.well-known/oauth-protected-resource");
        Assert.True(response.IsSuccessStatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("resource", out _));
        Assert.True(doc.RootElement.TryGetProperty("authorization_servers", out _));
    }

    [Fact]
    public async Task FullSmoke_UnauthorizedMcpRequest_Returns401()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(
            """{"jsonrpc":"2.0","method":"tools/list","params":{},"id":1}""",
            Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task WaitForWorkerAsync(int timeoutSeconds)
    {
        using var scope = _app!.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<OpenIddict.Abstractions.IOpenIddictApplicationManager>();
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var client = await manager.FindByClientIdAsync("demo-client-id");
            if (client != null) return;
            await Task.Delay(200);
        }
    }

    private string? _mcpSessionId;
    private string? _lastMcpSessionId;

    private async Task<JsonDocument> SendMcpRequest(string method, object @params)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        // Get a token to authenticate
        var tokenStore = _app!.Services.GetRequiredService<ISecureTokenStore>();
        var tokenData = await tokenStore.GetTokenAsync("playwright-test-session", "OAuth", CancellationToken.None);
        if (tokenData != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenData.AccessToken);
        }

        if (_mcpSessionId != null)
        {
            request.Headers.Add("Mcp-Session-Id", _mcpSessionId);
        }

        var body = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method,
            @params,
            id = 1
        });
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        // Capture session ID from response header
        if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessionIds))
        {
            _lastMcpSessionId = sessionIds.FirstOrDefault();
        }

        // MCP SDK 2.0 uses SSE format for streamable HTTP — extract JSON from SSE data lines
        if (content.StartsWith("data: ") || content.Contains("\ndata: "))
        {
            var lines = content.Split('\n');
            var dataLine = lines.FirstOrDefault(l => l.StartsWith("data: "));
            if (dataLine != null) content = dataLine["data: ".Length..].Trim();
        }

        Assert.False(string.IsNullOrWhiteSpace(content), $"MCP response was empty. Status: {response.StatusCode}, Content-Type: {response.Content.Headers.ContentType}");
        Assert.True(response.IsSuccessStatusCode, $"MCP request failed. Status: {response.StatusCode}, Content: {content}");

        return JsonDocument.Parse(content);
    }

    private static int GetRandomUnusedPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}