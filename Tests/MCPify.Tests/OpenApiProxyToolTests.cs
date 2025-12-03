using MCPify.Core;
using MCPify.Core.Auth;
using MCPify.Schema;
using MCPify.Tools;
using Microsoft.OpenApi.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using RichardSzalay.MockHttp;
using System.Text.Json;

namespace MCPify.Tests;

public class OpenApiProxyToolTests
{
    private readonly MockHttpMessageHandler _mockHttp;
    private readonly Mock<IJsonSchemaGenerator> _mockSchema;
    private readonly HttpClient _httpClient;

    public OpenApiProxyToolTests()
    {
        _mockHttp = new MockHttpMessageHandler();
        _httpClient = _mockHttp.ToHttpClient();
        _mockSchema = new Mock<IJsonSchemaGenerator>();

        // Setup default schema behavior
        _mockSchema.Setup(s => s.GenerateInputSchema(It.IsAny<OpenApiOperation>()))
            .Returns(JsonDocument.Parse("{}").RootElement);
    }

    [Fact]
    public async Task InvokeAsync_AppliesAuthentication()
    {
        // Arrange
        var descriptor = new OpenApiOperationDescriptor(
            Name: "test_tool",
            Route: "/test",
            Method: OperationType.Get,
            Operation: new OpenApiOperation()
        );

        var authMock = new Mock<IAuthenticationProvider>();
        var options = new McpifyOptions();

        var tool = new OpenApiProxyTool(
            descriptor,
            "http://api.com",
            _httpClient,
            _mockSchema.Object,
            options,
            authMock.Object
        );

        _mockHttp.When("http://api.com/test")
            .Respond("application/json", "{\"success\": true}");

        var mockServer = new Mock<McpServer>();
        var dummyRequest = new JsonRpcRequest { Method = "tools/call", Id = new RequestId(1) };

        // Act
        await tool.InvokeAsync(new RequestContext<CallToolRequestParams>(mockServer.Object, dummyRequest)
        {
            Params = new CallToolRequestParams { Name = "test_tool" }
        }, CancellationToken.None);

        // Assert
        authMock.Verify(a => a.Apply(It.IsAny<HttpRequestMessage>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_CallsCorrectUrl()
    {
        // Arrange
        var descriptor = new OpenApiOperationDescriptor(
            Name: "get_user",
            Route: "/users/{id}",
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
            "http://api.com",
            _httpClient,
            _mockSchema.Object,
            new McpifyOptions()
        );

        _mockHttp.Expect("http://api.com/users/123")
            .Respond("application/json", "{\"id\": 123}");

        var args = new Dictionary<string, object> { { "id", "123" } };
        var jsonArgs = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(args));

        var mockServer = new Mock<McpServer>();
        var dummyRequest = new JsonRpcRequest { Method = "tools/call", Id = new RequestId(1) };

        // Act
        var result = await tool.InvokeAsync(new RequestContext<CallToolRequestParams>(mockServer.Object, dummyRequest)
        {
            Params = new CallToolRequestParams
            {
                Name = "get_user",
                Arguments = jsonArgs
            }
        }, CancellationToken.None);

        // Assert
        _mockHttp.VerifyNoOutstandingExpectation();
        Assert.NotEqual(true, result.IsError);
    }
}
