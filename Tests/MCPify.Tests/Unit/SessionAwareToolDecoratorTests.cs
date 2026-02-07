using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MCPify.Core;
using MCPify.Hosting;
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

    private static string ReadText(CallToolResult result)
    {
        var block = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
        return block.Text;
    }

    private static RequestContext<CallToolRequestParams> CreateContext(IServiceProvider services, string sessionId)
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
        context.Params = new CallToolRequestParams
        {
            Name = "probe_context",
            Arguments = new Dictionary<string, JsonElement>
            {
                ["sessionId"] = JsonSerializer.SerializeToElement(sessionId)
            }
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
}
