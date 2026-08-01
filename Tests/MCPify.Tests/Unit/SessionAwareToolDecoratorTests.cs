using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.Json;
using MCPify.Core;
using MCPify.Core.Auth;
using MCPify.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;

namespace MCPify.Tests.Unit;

public class SessionAwareToolDecoratorTests
{
    [Fact]
    public async Task InvokeAsync_ClearsAndRestoresContextPerCall()
    {
        // Arrange
        var services = new ServiceCollection();
        var accessor = new McpContextAccessor();
        var httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };

        services.AddSingleton<IMcpContextAccessor>(accessor);
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);

        var provider = services.BuildServiceProvider();
        var innerTool = new ContextProbeTool(accessor);
        var decorator = new SessionAwareToolDecorator(innerTool, provider);

        // First call with a bearer token
        httpContextAccessor.HttpContext!.Request.Headers["Authorization"] = "Bearer token-one";
        var firstResult = await decorator.InvokeAsync(CreateContext(provider, "session-one"), CancellationToken.None);

        // Second call without Authorization header should not reuse token from prior call
        httpContextAccessor.HttpContext = new DefaultHttpContext();
        var secondResult = await decorator.InvokeAsync(CreateContext(provider, "session-two"), CancellationToken.None);

        // Assert
        Assert.Equal("session-one|Bearer token-one", ReadText(firstResult));
        Assert.Equal("session-two|(null)", ReadText(secondResult));
        Assert.Null(accessor.SessionId);
        Assert.Null(accessor.AccessToken);
    }

    [Fact]
    public async Task InvokeAsync_UsesBridgedDefaultSession_InStdio_WhenSessionMissing()
    {
        var services = new ServiceCollection();
        var accessor = new McpContextAccessor();
        var httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var sessionMap = new MCPify.Core.Session.InMemorySessionMap();
        sessionMap.UpgradeSession(Constants.DefaultSessionId, "bridged-session");

        services.AddSingleton<IMcpContextAccessor>(accessor);
        services.AddSingleton<IHttpContextAccessor>(httpContextAccessor);
        services.AddSingleton<MCPify.Core.Session.ISessionMap>(sessionMap);
        services.AddSingleton(new McpifyOptions { Transport = McpTransportType.Stdio });

        var provider = services.BuildServiceProvider();
        var innerTool = new ContextProbeTool(accessor);
        var decorator = new SessionAwareToolDecorator(innerTool, provider);

        var result = await decorator.InvokeAsync(CreateContext(provider, null), CancellationToken.None);

        Assert.Equal("bridged-session|(null)", ReadText(result));
    }

    [Fact]
    public async Task InvokeAsync_EnforcesScopeRequirement_WhenScopePresent()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationHandler, ScopeRequirementHandler>();
        var accessor = new McpContextAccessor();
        services.AddSingleton<IMcpContextAccessor>(accessor);
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        var provider = services.BuildServiceProvider();
        var innerTool = new ScopeProtectedTool(new[] { new ScopeRequirement("api.read") });
        var decorator = new SessionAwareToolDecorator(innerTool, provider);

        // Build a principal with the required scope
        var httpContextAccessor = provider.GetRequiredService<IHttpContextAccessor>();
        httpContextAccessor.HttpContext!.User = new ClaimsPrincipal(
            new ClaimsIdentity(new[] { new Claim("scope", "api.read api.write") }, "test"));

        var result = await decorator.InvokeAsync(CreateContext(provider, "session-1"), CancellationToken.None);

        Assert.True(result.IsError != true);
        Assert.Equal("ok", ReadText(result));
    }

    [Fact]
    public async Task InvokeAsync_RejectsScopeRequirement_WhenScopeMissing()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationHandler, ScopeRequirementHandler>();
        var accessor = new McpContextAccessor();
        services.AddSingleton<IMcpContextAccessor>(accessor);
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        var provider = services.BuildServiceProvider();
        var innerTool = new ScopeProtectedTool(new[] { new ScopeRequirement("api.admin") });
        var decorator = new SessionAwareToolDecorator(innerTool, provider);

        // Principal has only api.read — api.admin is missing
        var httpContextAccessor = provider.GetRequiredService<IHttpContextAccessor>();
        httpContextAccessor.HttpContext!.User = new ClaimsPrincipal(
            new ClaimsIdentity(new[] { new Claim("scope", "api.read") }, "test"));

        var result = await decorator.InvokeAsync(CreateContext(provider, "session-1"), CancellationToken.None);

        Assert.True(result.IsError == true);
        Assert.Contains("Insufficient scope", ReadText(result));
        Assert.Contains("api.admin", ReadText(result));
    }

    [Fact]
    public async Task InvokeAsync_AllowsTool_WhenNoScopeRequirements()
    {
        var services = new ServiceCollection();
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationHandler, ScopeRequirementHandler>();
        var accessor = new McpContextAccessor();
        services.AddSingleton<IMcpContextAccessor>(accessor);
        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        var provider = services.BuildServiceProvider();
        var innerTool = new ScopeProtectedTool(Array.Empty<ScopeRequirement>());
        var decorator = new SessionAwareToolDecorator(innerTool, provider);

        // No principal, no scopes — but tool has no requirements, so it should pass
        var result = await decorator.InvokeAsync(CreateContext(provider, "session-1"), CancellationToken.None);

        Assert.True(result.IsError != true);
        Assert.Equal("ok", ReadText(result));
    }

    private static string ReadText(CallToolResult result)
    {
        var block = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        return block.Text;
    }

    private static RequestContext<CallToolRequestParams> CreateContext(IServiceProvider services, string? sessionId)
    {
        var mockServer = new Mock<McpServer>();
        mockServer.SetupGet(s => s.Services).Returns(services);

        var jsonRpcRequestType = typeof(RequestContext<>).Assembly.GetTypes()
            .First(t => t.Name == "JsonRpcRequest" && !t.IsAbstract);
        var jsonRpcRequest = RuntimeHelpers.GetUninitializedObject(jsonRpcRequestType);

        var ctor = typeof(RequestContext<CallToolRequestParams>).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .First(c => c.GetParameters().Length == 2);

        var context = (RequestContext<CallToolRequestParams>)ctor.Invoke(new object?[] { mockServer.Object, jsonRpcRequest });
        context.Services = services;
        var arguments = new Dictionary<string, JsonElement>();
        if (!string.IsNullOrEmpty(sessionId))
        {
            arguments["sessionId"] = JsonSerializer.SerializeToElement(sessionId);
        }

        context.Params = new CallToolRequestParams
        {
            Name = "probe_context",
            Arguments = arguments
        };

        return context;
    }

    private sealed class ContextProbeTool(IMcpContextAccessor accessor) : McpServerTool
    {
        public override Tool ProtocolTool => new()
        {
            Name = "probe_context",
            Description = "Returns the current context values for testing.",
            InputSchema = JsonSerializer.SerializeToElement(new { type = "object" })
        };

        public override IReadOnlyList<object> Metadata => Array.Empty<object>();

        public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> context, CancellationToken token)
        {
            var session = accessor.SessionId ?? "(null)";
            var accessToken = accessor.AccessToken ?? "(null)";

            return new ValueTask<CallToolResult>(new CallToolResult
            {
                Content = new[] { new TextContentBlock { Text = $"{session}|{accessToken}" } }
            });
        }
    }

    private sealed class ScopeProtectedTool : McpServerTool
    {
        private readonly IReadOnlyList<object> _metadata;

        public ScopeProtectedTool(IEnumerable<ScopeRequirement> requirements)
        {
            _metadata = requirements.Cast<object>().ToList();
        }

        public override Tool ProtocolTool => new()
        {
            Name = "scope_protected",
            Description = "A tool protected by scope requirements.",
            InputSchema = JsonSerializer.SerializeToElement(new { type = "object" })
        };

        public override IReadOnlyList<object> Metadata => _metadata;

        public override ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> context, CancellationToken token)
        {
            return new ValueTask<CallToolResult>(new CallToolResult
            {
                Content = new[] { new TextContentBlock { Text = "ok" } }
            });
        }
    }
}
