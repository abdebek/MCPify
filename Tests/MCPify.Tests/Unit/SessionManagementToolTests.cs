using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using MCPify.Core;
using MCPify.Core.Session;
using MCPify.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;

namespace MCPify.Tests.Unit;

public class SessionManagementToolTests
{
    [Fact]
    public async Task Connect_UpdatesDefaultSessionBridge()
    {
        var services = new ServiceCollection();
        var accessor = new MockMcpContextAccessor();
        var sessionMap = new InMemorySessionMap();
        services.AddSingleton<IMcpContextAccessor>(accessor);
        services.AddSingleton<ISessionMap>(sessionMap);
        var provider = services.BuildServiceProvider();

        var tool = new SessionManagementTool();
        var context = CreateContext(provider, "session-from-connect");

        var result = await tool.InvokeAsync(context, CancellationToken.None);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("session-from-connect", text);
        Assert.Equal("session-from-connect", accessor.SessionId);
        Assert.Equal("session-from-connect", sessionMap.ResolvePrincipal(Constants.DefaultSessionId));
    }

    private static RequestContext<CallToolRequestParams> CreateContext(IServiceProvider services, string serverSessionId)
    {
        var mockServer = new Mock<McpServer>();
        mockServer.SetupGet(s => s.Services).Returns(services);
        mockServer.SetupGet(s => s.SessionId).Returns(serverSessionId);

        var jsonRpcRequestType = typeof(RequestContext<>).Assembly.GetTypes()
            .First(t => t.Name == "JsonRpcRequest" && !t.IsAbstract);
        var jsonRpcRequest = RuntimeHelpers.GetUninitializedObject(jsonRpcRequestType);

        var ctor = typeof(RequestContext<CallToolRequestParams>).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .First(c => c.GetParameters().Length == 2);

        var context = (RequestContext<CallToolRequestParams>)ctor.Invoke(new object?[] { mockServer.Object, jsonRpcRequest });
        context.Services = services;
        context.Params = new CallToolRequestParams
        {
            Name = "connect",
            Arguments = new Dictionary<string, System.Text.Json.JsonElement>()
        };

        return context;
    }
}
