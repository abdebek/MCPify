using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using MCPify.Core;
using MCPify.Core.Auth;
using MCPify.Schema;
using MCPify.Tests.Integration;
using MCPify.Tools;
using Microsoft.OpenApi.Models;

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
        var tool = new OpenApiProxyTool(
            descriptor,
            _apiServer.BaseUrl,
            _apiServer.CreateClient(),
            _schema,
            new McpifyOptions(),
            _ => auth
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
            new McpifyOptions()
        );

        var request = BuildRequest(tool, new Dictionary<string, object> { { "id", 123 } });
        var response = await _apiServer.CreateClient().SendAsync(request);
        var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(await response.Content.ReadAsStringAsync());

        Assert.True(response.IsSuccessStatusCode);
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
}
