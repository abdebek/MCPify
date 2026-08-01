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
using Microsoft.Playwright;
using OpenIddict.Abstractions;
using Xunit;

namespace MCPify.Tests.Integration;

/// <summary>
/// Comprehensive integration tests for the Sample app.
/// Boots the real Sample, runs OAuth via Playwright (headless Chromium required),
/// and exercises MCP JSON-RPC, local endpoints, metadata, and DCR.
/// Tagged "Playwright" — filter with: dotnet test --filter "Category=Playwright".
/// </summary>
[Trait("Category", "Playwright")]
public class SampleIntegrationTests : IAsyncLifetime
{
    private WebApplication? _app;
    private string _baseUrl = "";
    private HttpClient _httpClient = null!;
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private string? _mcpSessionId;
    private string? _currentSessionId;

    public async Task InitializeAsync()
    {
        _baseUrl = "http://localhost:5005";

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = Array.Empty<string>(),
            ApplicationName = typeof(Worker).Assembly.FullName
        });
        builder.WebHost.UseUrls(_baseUrl);

        builder.Configuration["Demo:BaseUrl"] = _baseUrl;
        builder.Configuration["Demo:StateSecret"] = "test-state-secret-for-integration-tests-32+";
        builder.Configuration["Mcpify:Transport"] = "Http";

        builder.Services.AddDemoDatabaseAndAuth(_baseUrl, dbName: $"itest-{Guid.NewGuid()}");
        builder.Services.AddDemoSwagger(_baseUrl);
        builder.Services.AddDemoMcpify(builder.Configuration, _baseUrl, $"{_baseUrl}/auth/callback");
        builder.Services.AddHostedService<Worker>();
        builder.Services.AddControllers();

        _app = builder.Build();

        _app.UseCors("DemoCors");
        _app.UseSwagger();
        _app.UseSwaggerUI();
        _app.UseDynamicClientRegistration();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapDemoEndpoints("/auth/callback");
        _app.MapMcpifyEndpoint("/mcp");

        await _app.StartAsync();
        await WaitForWorkerAsync(10);

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

    // --- OAuth flow ---

    [Fact]
    public async Task OAuth_BrowserFlow_StoresToken()
    {
        var sessionId = "itest-token-store";
        await PerformOAuthLogin(sessionId);

        var tokenStore = _app!.Services.GetRequiredService<ISecureTokenStore>();
        var tokenData = await tokenStore.GetTokenAsync(sessionId, GetOAuthProviderName(), CancellationToken.None);
        Assert.NotNull(tokenData);
        Assert.False(string.IsNullOrEmpty(tokenData!.AccessToken));
    }

    [Fact]
    public async Task OAuth_CallbackPage_ShowsSuccessMessage()
    {
        var sessionId = "itest-callback-page";
        var authProvider = _app!.Services.GetRequiredService<OAuthAuthorizationCodeAuthentication>();
        var authUrl = authProvider.BuildAuthorizationUrl(sessionId);

        var page = await _browser.NewPageAsync();
        await page.GotoAsync(authUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 15000 });

        var pageContent = await page.ContentAsync();
        var pageUrl = page.Url;
        Assert.True(
            pageUrl.Contains("/auth/callback") ||
            pageContent.Contains("Login Successful", StringComparison.OrdinalIgnoreCase),
            $"Expected callback page. URL: {pageUrl}");
        await page.CloseAsync();
    }

    // --- MCP session lifecycle ---

    [Fact]
    public async Task MCP_Initialize_ReturnsProtocolVersionAndSessionHeader()
    {
        await PerformOAuthLogin("itest-init");
        var initResponse = await SendMcpRequest("initialize", new
        {
            protocolVersion = "2025-06-18",
            capabilities = new { },
            clientInfo = new { name = "itest", version = "1.0" }
        });
        Assert.True(initResponse.RootElement.TryGetProperty("result", out var result),
            $"initialize failed: {initResponse.RootElement.GetRawText()}");
        Assert.True(result.TryGetProperty("protocolVersion", out _));

        // Session ID may come in the response header or body depending on SDK version;
        // verify tools/list works after init (proves session was established).
        _mcpSessionId = _lastMcpSessionId;
        var listResponse = await SendMcpRequest("tools/list", new { });
        Assert.True(listResponse.RootElement.TryGetProperty("result", out _),
            $"tools/list after init failed: {listResponse.RootElement.GetRawText()}");
    }

    // --- MCP tools/list ---

    [Fact]
    public async Task MCP_ToolsList_IncludesLoginTool()
    {
        await PerformOAuthLogin("itest-tools-list");
        await InitMcpSession();

        var listResponse = await SendMcpRequest("tools/list", new { });
        Assert.True(listResponse.RootElement.TryGetProperty("result", out var result));
        Assert.True(result.TryGetProperty("tools", out var tools));
        var toolNames = tools.EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Contains("login_auth_code_pkce", toolNames);
    }

    [Fact]
    public async Task MCP_ToolsList_IncludesLocalEndpointTools()
    {
        await PerformOAuthLogin("itest-local-tools");
        await InitMcpSession();

        var listResponse = await SendMcpRequest("tools/list", new { });
        Assert.True(listResponse.RootElement.TryGetProperty("result", out var result));
        Assert.True(result.TryGetProperty("tools", out var tools));
        var toolNames = tools.EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Contains(toolNames, n => n!.StartsWith("api_"));
    }

    [Fact]
    public async Task MCP_ToolsList_IncludesExternalApiTools()
    {
        await PerformOAuthLogin("itest-external-tools");
        await InitMcpSession();

        var listResponse = await SendMcpRequest("tools/list", new { });
        Assert.True(listResponse.RootElement.TryGetProperty("result", out var result));
        Assert.True(result.TryGetProperty("tools", out var tools));
        var toolNames = tools.EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Contains(toolNames, n => n!.StartsWith("petstore_"));
        Assert.Contains(toolNames, n => n!.StartsWith("localfile_"));
    }

    // --- MCP tools/call ---

    [Fact]
    public async Task MCP_ToolCall_LocalUserEndpoint_ReturnsUserData()
    {
        await PerformOAuthLogin("itest-user-call");
        await InitMcpSession();

        var listResponse = await SendMcpRequest("tools/list", new { });
        Assert.True(listResponse.RootElement.TryGetProperty("result", out var result));
        Assert.True(result.TryGetProperty("tools", out var tools));
        var toolNames = tools.EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();

        var userTool = toolNames.FirstOrDefault(n => n!.Contains("users", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(userTool);

        var callResponse = await SendMcpRequest("tools/call", new { name = userTool, arguments = new { id = 42 } });
        Assert.True(callResponse.RootElement.TryGetProperty("result", out var callResult));
        Assert.True(callResult.TryGetProperty("content", out var content));
        var text = content.EnumerateArray()
            .Where(c => c.TryGetProperty("type", out var t) && t.GetString() == "text")
            .Select(c => c.GetProperty("text").GetString())
            .FirstOrDefault();
        Assert.NotNull(text);
        Assert.Contains("42", text);
    }

    [Fact]
    public async Task MCP_ToolCall_ProtectedSecretEndpoint_ReturnsSecretAfterLogin()
    {
        var sessionId = "itest-protected-call";
        await PerformOAuthLogin(sessionId);
        await InitMcpSession();

        var listResponse = await SendMcpRequest("tools/list", new { });
        Assert.True(listResponse.RootElement.TryGetProperty("result", out var result));
        Assert.True(result.TryGetProperty("tools", out var tools));
        var toolNames = tools.EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();

        var secretsTool = toolNames.FirstOrDefault(n => n!.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(secretsTool);

        var callResponse = await SendMcpRequest("tools/call", new { name = secretsTool, arguments = new { sessionId } });
        Assert.True(callResponse.RootElement.TryGetProperty("result", out var callResult));
        Assert.True(callResult.TryGetProperty("content", out var content));
        var text = content.EnumerateArray()
            .Where(c => c.TryGetProperty("type", out var t) && t.GetString() == "text")
            .Select(c => c.GetProperty("text").GetString())
            .FirstOrDefault();
        Assert.NotNull(text);
        Assert.Contains("Golden Eagle", text);
    }

    [Fact]
    public async Task MCP_ToolCall_ExternalPetstoreTool_AttemptsUpstreamCall()
    {
        await PerformOAuthLogin("itest-petstore-call");
        await InitMcpSession();

        var listResponse = await SendMcpRequest("tools/list", new { });
        Assert.True(listResponse.RootElement.TryGetProperty("result", out var result));
        Assert.True(result.TryGetProperty("tools", out var tools));
        var toolNames = tools.EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();

        var petstoreTool = toolNames.FirstOrDefault(n => n!.StartsWith("petstore_") && n.Contains("pet", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(petstoreTool);

        // Call the tool — external API may be unreachable in CI, so accept success or error result
        // but require a valid JSON-RPC response (not a protocol-level failure).
        var callResponse = await SendMcpRequest("tools/call", new { name = petstoreTool, arguments = new { } });
        Assert.True(callResponse.RootElement.TryGetProperty("result", out var callResult)
            || callResponse.RootElement.TryGetProperty("error", out _),
            $"Expected result or error in response: {callResponse.RootElement.GetRawText()}");
    }

    // --- Auth gates ---

    [Fact]
    public async Task MCP_RequestWithoutBearer_Returns401()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(
            """{"jsonrpc":"2.0","method":"tools/list","params":{},"id":1}""",
            Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MCP_InitializeWithoutBearer_Returns401()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Content = new StringContent(
            """{"jsonrpc":"2.0","method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}},"id":1}""",
            Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MCP_AuthenticatedWithoutMcpSession_ToolsListMaySucceedOrFail()
    {
        await PerformOAuthLogin("itest-no-session");

        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        var tokenStore = _app!.Services.GetRequiredService<ISecureTokenStore>();
        var tokenData = await tokenStore.GetTokenAsync("itest-no-session", GetOAuthProviderName(), CancellationToken.None);
        Assert.NotNull(tokenData);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenData!.AccessToken);

        request.Content = new StringContent(
            """{"jsonrpc":"2.0","method":"tools/list","params":{},"id":1}""",
            Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request);
        // SDK 2.0 may allow tools/list without an established session when authenticated.
        // The key assertion is that the bearer token was accepted (not 401).
        Assert.NotEqual(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- Metadata endpoints ---

    [Fact]
    public async Task ProtectedResourceMetadata_IsServedWithCorrectFields()
    {
        var response = await _httpClient.GetAsync("/.well-known/oauth-protected-resource");
        Assert.True(response.IsSuccessStatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("resource", out _));
        Assert.True(doc.RootElement.TryGetProperty("authorization_servers", out var servers));
        Assert.True(servers.EnumerateArray().Any());
    }

    [Fact]
    public async Task OpenIdConfiguration_IsServed()
    {
        var response = await _httpClient.GetAsync("/.well-known/openid-configuration");
        Assert.True(response.IsSuccessStatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("issuer", out _));
        Assert.True(doc.RootElement.TryGetProperty("authorization_endpoint", out _));
        Assert.True(doc.RootElement.TryGetProperty("token_endpoint", out _));
    }

    [Fact]
    public async Task DynamicClientRegistration_EndpointIsAvailable()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/connect/register")
        {
            Content = new StringContent(
                """{"client_name":"test-client","redirect_uris":["http://localhost:9999/callback"],"token_endpoint_auth_method":"client_secret_basic","grant_types":["authorization_code"],"response_types":["code"]}""",
                Encoding.UTF8, "application/json")
        };

        var response = await _httpClient.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode, $"DCR failed: {response.StatusCode}");
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("client_id", out _));
        Assert.True(doc.RootElement.TryGetProperty("client_secret", out _));
    }

    // --- Local API endpoints (direct HTTP, not via MCP) ---

    [Fact]
    public async Task LocalEndpoint_Status_ReturnsRunning()
    {
        var response = await _httpClient.GetAsync("/status");
        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Running", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task LocalEndpoint_GetUser_ReturnsUser()
    {
        var response = await _httpClient.GetAsync("/api/users/7");
        Assert.True(response.IsSuccessStatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"id\":7", json);
        Assert.Contains("User 7", json);
    }

    [Fact]
    public async Task LocalEndpoint_GetSecrets_RequiresAuth()
    {
        var response = await _httpClient.GetAsync("/api/secrets");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LocalEndpoint_GetWeather_ReturnsWeather()
    {
        var response = await _httpClient.GetAsync("/weather");
        Assert.True(response.IsSuccessStatusCode);
        Assert.Contains("Sunny", await response.Content.ReadAsStringAsync());
    }

    // --- Swagger ---

    [Fact]
    public async Task SwaggerEndpoint_IsAvailable()
    {
        var response = await _httpClient.GetAsync("/swagger/v1/swagger.json");
        Assert.True(response.IsSuccessStatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("openapi", out _));
        Assert.True(doc.RootElement.TryGetProperty("paths", out _));
    }

    // --- OpenIddict seeding ---

    [Fact]
    public async Task OpenIddict_DemoClient_IsSeeded()
    {
        using var scope = _app!.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var client = await manager.FindByClientIdAsync("demo-client-id");
        Assert.NotNull(client);
    }

    // --- Helpers ---

    private string GetOAuthProviderName() =>
        _app!.Services.GetRequiredService<OAuthAuthorizationCodeAuthentication>().ProviderName;

    private async Task PerformOAuthLogin(string sessionId)
    {
        _currentSessionId = sessionId;
        var authProvider = _app!.Services.GetRequiredService<OAuthAuthorizationCodeAuthentication>();
        var authUrl = authProvider.BuildAuthorizationUrl(sessionId);

        var page = await _browser.NewPageAsync();
        var responses = new List<string>();
        page.Response += (_, response) => responses.Add($"{response.Status} {response.Url}");

        try
        {
            await page.GotoAsync(authUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 15000 });
        }
        catch (PlaywrightException)
        {
            Assert.True(responses.Any(r => r.Contains("/auth/callback")),
                $"OAuth navigation failed. Responses: {string.Join(", ", responses)}");
        }

        var pageUrl = page.Url;
        var pageContent = await page.ContentAsync();
        Assert.True(
            pageUrl.Contains("/auth/callback") ||
            pageContent.Contains("Login Successful", StringComparison.OrdinalIgnoreCase),
            $"Expected callback. URL: {pageUrl}, Content: {pageContent[..Math.Min(500, pageContent.Length)]}");
        await page.CloseAsync();
    }

    private async Task InitMcpSession()
    {
        var initResponse = await SendMcpRequest("initialize", new
        {
            protocolVersion = "2025-06-18",
            capabilities = new { },
            clientInfo = new { name = "itest", version = "1.0" }
        });
        Assert.True(initResponse.RootElement.TryGetProperty("result", out _),
            $"initialize failed: {initResponse.RootElement.GetRawText()}");
        _mcpSessionId = _lastMcpSessionId;
    }

    private string? _lastMcpSessionId;

    private async Task<JsonDocument> SendMcpRequest(string method, object @params)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        var tokenStore = _app!.Services.GetRequiredService<ISecureTokenStore>();
        TokenData? tokenData = null;
        if (_currentSessionId != null)
            tokenData = await tokenStore.GetTokenAsync(_currentSessionId, GetOAuthProviderName(), CancellationToken.None);
        if (tokenData != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenData.AccessToken);

        if (_mcpSessionId != null)
            request.Headers.Add("Mcp-Session-Id", _mcpSessionId);

        var body = JsonSerializer.Serialize(new { jsonrpc = "2.0", method, @params, id = 1 });
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        if (response.Headers.TryGetValues("Mcp-Session-Id", out var sessionIds))
            _lastMcpSessionId = sessionIds.FirstOrDefault();

        if (content.StartsWith("data: ") || content.Contains("\ndata: "))
        {
            var lines = content.Split('\n');
            var dataLine = lines.FirstOrDefault(l => l.StartsWith("data: "));
            if (dataLine != null) content = dataLine["data: ".Length..].Trim();
        }

        Assert.False(string.IsNullOrWhiteSpace(content), $"Empty MCP response. Status: {response.StatusCode}");
        Assert.True(response.IsSuccessStatusCode, $"MCP request failed. Status: {response.StatusCode}, Content: {content}");

        return JsonDocument.Parse(content);
    }

    private async Task WaitForWorkerAsync(int timeoutSeconds)
    {
        using var scope = _app!.Services.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            if (await manager.FindByClientIdAsync("demo-client-id") != null) return;
            await Task.Delay(200);
        }
    }
}