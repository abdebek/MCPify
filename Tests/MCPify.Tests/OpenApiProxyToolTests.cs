using System.Net.Http;
using System.Net.Http.Headers;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MCPify.Core;
using MCPify.Core.Auth;
using MCPify.Core.Auth.TokenProviders;
using MCPify.Schema;
using MCPify.Tests.Integration;
using MCPify.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;

namespace MCPify.Tests;

public class OpenApiProxyToolTests : IAsyncLifetime
{
    private readonly TestApiServer _apiServer = new();
    private readonly IJsonSchemaGenerator _schema = new DefaultJsonSchemaGenerator();

    public async Task InitializeAsync() => await _apiServer.StartAsync();

    public async Task DisposeAsync() => await _apiServer.DisposeAsync();

    [Fact]
    public async Task InvokeAsync_AppliesAuthentication()
    {
        var descriptor = new OpenApiOperationDescriptor(
            Name: "auth_check",
            Route: "/auth-check",
            Method: OperationType.Get,
            Operation: new OpenApiOperation()
        );

        var auth = new TrackingAuthProvider();
        var serviceProvider = new ServiceCollection()
            .AddScoped<IMcpContextAccessor, McpContextAccessor>()
            .BuildServiceProvider();

        var tokenProvider = new AuthenticationFactoryTokenProvider(_ => auth, serviceProvider);
        var tool = new OpenApiProxyTool(
            descriptor,
            _apiServer.BaseUrl,
            _apiServer.CreateClient(),
            _schema,
            new McpifyOptions(),
            tokenProvider
        );

        var request = BuildRequest(tool, null);
        await auth.ApplyAsync(request);

        var response = await _apiServer.CreateClient().SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(json);

        Assert.Equal(1, auth.ApplyCount);
        Assert.Equal("Bearer test-token", payload?["authorization"]);
    }

    [Fact]
    public async Task InvokeAsync_ServerManaged_AppliesApiKeyHeader_NotJustAuthorization()
    {
        var descriptor = new OpenApiOperationDescriptor(
            Name: "apikey_check",
            Route: "/apikey-check",
            Method: OperationType.Get,
            Operation: new OpenApiOperation()
        );

        var apiKeyAuth = new ApiKeyAuthentication("X-API-Key", "secret-key-123", ApiKeyLocation.Header);
        var serviceProvider = new ServiceCollection()
            .AddSingleton<IMcpContextAccessor>(new MockMcpContextAccessor())
            .BuildServiceProvider();

        var tokenProvider = new AuthenticationFactoryTokenProvider(_ => apiKeyAuth, serviceProvider);
        var tool = new OpenApiProxyTool(
            descriptor,
            _apiServer.BaseUrl,
            _apiServer.CreateClient(),
            _schema,
            new McpifyOptions(),
            tokenProvider
        );

        var services = new ServiceCollection().BuildServiceProvider();
        var context = CreateContext(services, descriptor.Name, null);
        var result = await tool.InvokeAsync(context, CancellationToken.None);

        Assert.True(result.IsError != true);
        var content = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(content);

        Assert.Equal("secret-key-123", payload?["apiKey"]);
    }

    [Fact]
    public async Task InvokeAsync_CallsCorrectUrl()
    {
        var descriptor = new OpenApiOperationDescriptor(
            Name: "get_user",
            Route: "/users/{id:int}",
            Method: OperationType.Get,
            Operation: new OpenApiOperation
            {
                Parameters = new List<OpenApiParameter>
                {
                    new OpenApiParameter { Name = "id", In = ParameterLocation.Path }
                }
            }
        );

        var tool = new OpenApiProxyTool(
            descriptor,
            _apiServer.BaseUrl,
            _apiServer.CreateClient(),
            _schema,
            new McpifyOptions(),
            NoTokenProvider.Instance
        );

        var request = BuildRequest(tool, new Dictionary<string, object> { { "id", 123 } });
        var response = await _apiServer.CreateClient().SendAsync(request);
        var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(await response.Content.ReadAsStringAsync());

        Assert.True(response.IsSuccessStatusCode);
        Assert.Equal("/users/123", payload?["path"]?.ToString());
    }

    [Fact]
    public async Task InvokeAsync_ReturnsInputError_WhenRequiredPathParameterMissing()
    {
        var descriptor = new OpenApiOperationDescriptor(
            Name: "get_user",
            Route: "/users/{id:int}",
            Method: OperationType.Get,
            Operation: new OpenApiOperation
            {
                Parameters = new List<OpenApiParameter>
                {
                    new OpenApiParameter { Name = "id", In = ParameterLocation.Path, Required = true }
                }
            }
        );

        var services = new ServiceCollection().BuildServiceProvider();
        var tool = new OpenApiProxyTool(
            descriptor,
            _apiServer.BaseUrl,
            _apiServer.CreateClient(),
            _schema,
            new McpifyOptions(),
            NoTokenProvider.Instance
        );

        var context = CreateContext(services, descriptor.Name, null);
        var result = await tool.InvokeAsync(context, CancellationToken.None);

        Assert.True(result.IsError == true);
        var content = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains("path parameter", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvokeAsync_PathParameterLookup_IsCaseInsensitive()
    {
        var descriptor = new OpenApiOperationDescriptor(
            Name: "get_user",
            Route: "/users/{id:int}",
            Method: OperationType.Get,
            Operation: new OpenApiOperation
            {
                Parameters = new List<OpenApiParameter>
                {
                    new OpenApiParameter { Name = "id", In = ParameterLocation.Path, Required = true }
                }
            }
        );

        var services = new ServiceCollection().BuildServiceProvider();
        var tool = new OpenApiProxyTool(
            descriptor,
            _apiServer.BaseUrl,
            _apiServer.CreateClient(),
            _schema,
            new McpifyOptions(),
            NoTokenProvider.Instance
        );

        var context = CreateContext(services, descriptor.Name, new Dictionary<string, object> { ["ID"] = 123 });
        var result = await tool.InvokeAsync(context, CancellationToken.None);
        var content = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(content);

        Assert.True(result.IsError != true);
        Assert.Equal("/users/123", payload?["path"]?.ToString());
    }

    private static HttpRequestMessage BuildRequest(OpenApiProxyTool tool, object? args)
    {
        var method = typeof(OpenApiProxyTool).GetMethod("BuildHttpRequest", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var dict = args == null
            ? new Dictionary<string, JsonElement>()
            : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(args))!;
        return (HttpRequestMessage)method.Invoke(tool, new object?[] { dict })!;
    }

    private static RequestContext<CallToolRequestParams> CreateContext(IServiceProvider services, string name, Dictionary<string, object>? args)
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
            Name = name,
            Arguments = args == null
                ? new Dictionary<string, JsonElement>()
                : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(args))
        };

        return context;
    }

    private sealed class TrackingAuthProvider : IAuthenticationProvider
    {
        public int ApplyCount { get; private set; }

        public Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
        {
            ApplyCount++;
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpAuthProvider : IAuthenticationProvider
    {
        public Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task AuthenticationFactoryTokenProvider_ReturnsFalse_WhenProviderIsNoOp()
    {
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var tokenProvider = new AuthenticationFactoryTokenProvider(_ => new NoOpAuthProvider(), serviceProvider);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com");

        var result = await tokenProvider.ApplyAsync(request, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task AuthenticationFactoryTokenProvider_ReturnsTrue_WhenProviderAppliesAuth()
    {
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var tokenProvider = new AuthenticationFactoryTokenProvider(_ => new TrackingAuthProvider(), serviceProvider);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com");

        var result = await tokenProvider.ApplyAsync(request, CancellationToken.None);

        Assert.True(result);
        Assert.NotNull(request.Headers.Authorization);
    }
}
