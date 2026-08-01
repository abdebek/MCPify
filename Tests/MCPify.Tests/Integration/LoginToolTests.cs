using MCPify.Core;
using MCPify.Core.Auth;
using MCPify.Core.Auth.OAuth;
using MCPify.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using System.Text.Json;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace MCPify.Tests.Integration;

public class LoginToolTests
{
    private static RequestContext<CallToolRequestParams> CreateContext(CallToolRequestParams @params, IServiceProvider services)
    {
        var mockServer = new Mock<McpServer>();
        mockServer.SetupGet(s => s.Services).Returns(services);
        
        // Find concrete JsonRpcRequest
        var jsonRpcRequestType = typeof(RequestContext<>).Assembly.GetTypes()
            .First(t => t.Name == "JsonRpcRequest" && !t.IsAbstract);
        var jsonRpcRequest = RuntimeHelpers.GetUninitializedObject(jsonRpcRequestType);

        var ctor = typeof(RequestContext<CallToolRequestParams>).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(c => c.GetParameters().Length == 2);
            
        if (ctor == null)
        {
             throw new Exception("Could not find RequestContext constructor");
        }

        var context = (RequestContext<CallToolRequestParams>)ctor.Invoke(new object?[] { mockServer.Object, jsonRpcRequest });
        context.Params = @params;
        context.Services = services;
        return context;
    }

    [Fact]
    public async Task LoginTool_ShouldPollAndReturnSuccess_WhenTokenAppears()
    {
        // Arrange
        var services = new ServiceCollection();

        var tokenStore = new InMemoryTokenStore();
        var accessor = new MockMcpContextAccessor { SessionId = "default" };
        var auth = new StubOAuthAuthorization(tokenStore, accessor);

        services.AddSingleton(accessor);
        services.AddSingleton<IMcpContextAccessor>(accessor);
        services.AddSingleton<OAuthAuthorizationCodeAuthentication>(auth);
        services.AddSingleton<ISecureTokenStore>(tokenStore);
        services.AddSingleton<LoginTool>();
        // Always enter the poll loop. LoginBrowserBehavior.Auto on headless CI (no DISPLAY)
        // returns the auth URL immediately without waiting for a token — that is correct product
        // behavior for agents, but this test specifically covers the polling success path.
        services.AddSingleton(new McpifyOptions { LoginBrowserBehavior = BrowserLaunchBehavior.Always });

        var provider = services.BuildServiceProvider();
        var tool = provider.GetRequiredService<LoginTool>();

        var arguments = new Dictionary<string, JsonElement>();
        var callToolParams = new CallToolRequestParams { Name = "login", Arguments = arguments };
        var context = CreateContext(callToolParams, provider);

        // Act
        var toolTask = tool.InvokeAsync(context, CancellationToken.None);

        await Task.Delay(500);
        await tokenStore.SaveTokenAsync("default", auth.ProviderName, new TokenData("test-access-token", null, null), CancellationToken.None);

        var result = await toolTask;

        // Assert
        Assert.True(result.IsError != true);
        Assert.Single(result.Content);
        var textContent = Assert.IsType<TextContentBlock>(result.Content[0]);
        Assert.Contains("Login successful", textContent.Text);
        Assert.Contains("default", textContent.Text);
    }

    [Fact]
    public async Task LoginTool_ShouldTimeout_WhenNoTokenAppears()
    {
        // Arrange
        var services = new ServiceCollection();

        var tokenStore = new InMemoryTokenStore();
        var accessor = new MockMcpContextAccessor { SessionId = "default" };
        var auth = new StubOAuthAuthorization(tokenStore, accessor);

        services.AddSingleton(accessor);
        services.AddSingleton<IMcpContextAccessor>(accessor);
        services.AddSingleton<OAuthAuthorizationCodeAuthentication>(auth);
        services.AddSingleton<ISecureTokenStore>(tokenStore);
        services.AddSingleton<LoginTool>();
        // Never open browser → return URL immediately (stable on headless CI and local)
        services.AddSingleton(new McpifyOptions { LoginBrowserBehavior = BrowserLaunchBehavior.Never });

        var provider = services.BuildServiceProvider();
        var tool = provider.GetRequiredService<LoginTool>();

        var arguments = new Dictionary<string, JsonElement>();
        var callToolParams = new CallToolRequestParams { Name = "login", Arguments = arguments };
        var context = CreateContext(callToolParams, provider);

        // Act
        var result = await tool.InvokeAsync(context, CancellationToken.None);

        // Assert
        Assert.True(result.IsError != true);
        Assert.Single(result.Content);
        var textContent = Assert.IsType<TextContentBlock>(result.Content[0]);
        
        Assert.DoesNotContain("Login successful", textContent.Text);
        Assert.Contains("http://auth/authorize?foo=bar", textContent.Text);
    }

    [Fact]
    public async Task LoginTool_UsesDefaultSessionId_WhenNoSessionIsAvailable()
    {
        var services = new ServiceCollection();

        var tokenStore = new InMemoryTokenStore();
        var accessor = new MockMcpContextAccessor { SessionId = null };
        var auth = new StubOAuthAuthorization(tokenStore, accessor);

        services.AddSingleton(accessor);
        services.AddSingleton<IMcpContextAccessor>(accessor);
        services.AddSingleton<OAuthAuthorizationCodeAuthentication>(auth);
        services.AddSingleton<ISecureTokenStore>(tokenStore);
        services.AddSingleton<LoginTool>();
        services.AddSingleton(new McpifyOptions { LoginBrowserBehavior = BrowserLaunchBehavior.Never });

        var provider = services.BuildServiceProvider();
        var tool = provider.GetRequiredService<LoginTool>();

        var arguments = new Dictionary<string, JsonElement>();
        var callToolParams = new CallToolRequestParams { Name = "login", Arguments = arguments };
        var context = CreateContext(callToolParams, provider);

        await tool.InvokeAsync(context, CancellationToken.None);

        Assert.Equal(Constants.DefaultSessionId, auth.LastSessionId);
        Assert.Equal(Constants.DefaultSessionId, accessor.SessionId);
    }

    [Fact]
    public async Task LoginTool_CopiesTokenToDefaultSession_InStdioMode()
    {
        var services = new ServiceCollection();

        var tokenStore = new InMemoryTokenStore();
        var accessor = new MockMcpContextAccessor { SessionId = "session-x" };
        var auth = new StubOAuthAuthorization(tokenStore, accessor);

        services.AddSingleton(accessor);
        services.AddSingleton<IMcpContextAccessor>(accessor);
        services.AddSingleton<OAuthAuthorizationCodeAuthentication>(auth);
        services.AddSingleton<ISecureTokenStore>(tokenStore);
        services.AddSingleton<LoginTool>();
        services.AddSingleton(new McpifyOptions
        {
            Transport = McpTransportType.Stdio,
            LoginBrowserBehavior = BrowserLaunchBehavior.Always
        });

        await tokenStore.SaveTokenAsync("session-x", auth.ProviderName, new TokenData("token-x", "refresh-x", DateTimeOffset.UtcNow.AddMinutes(10)));

        var provider = services.BuildServiceProvider();
        var tool = provider.GetRequiredService<LoginTool>();

        var arguments = new Dictionary<string, JsonElement>();
        var callToolParams = new CallToolRequestParams { Name = "login", Arguments = arguments };
        var context = CreateContext(callToolParams, provider);

        var result = await tool.InvokeAsync(context, CancellationToken.None);

        Assert.True(result.IsError != true);

        var copied = await tokenStore.GetTokenAsync(Constants.DefaultSessionId, auth.ProviderName);
        Assert.NotNull(copied);
        Assert.Equal("token-x", copied!.AccessToken);
    }

        private sealed class StubOAuthAuthorization : OAuthAuthorizationCodeAuthentication
        {
            public string? LastSessionId { get; private set; }

            public StubOAuthAuthorization(ISecureTokenStore store, IMcpContextAccessor accessor)
                : base(
                    "client",
                    "http://auth",
                    "http://token",
                    "scope",
                    store,
                    accessor,
                    redirectUri: "http://callback",
                    stateSecret: "test-state-secret-key-for-hmac-signing-32+")
            {
            }

            public override string BuildAuthorizationUrl(string sessionId)
            {
                LastSessionId = sessionId;
                return "http://auth/authorize?foo=bar";
            }
        }
}
