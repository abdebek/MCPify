using MCPify.Hosting;
using MCPify.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using Xunit;

namespace MCPify.Tests;

public class DelegateToolTests
{
    [Fact]
    public void Create_SetsNameAndDescription()
    {
        var tool = DelegateToolBuilder.Create("echo", "Echoes input", (_, ct) => ValueTask.FromResult("ok"));
        Assert.Equal("echo", tool.ProtocolTool.Name);
        Assert.Equal("Echoes input", tool.ProtocolTool.Description);
    }

    [Fact]
    public void Create_WithCustomSchema_SetsInputSchema()
    {
        var schema = new { type = "object", properties = new { text = new { type = "string" } } };
        var tool = DelegateToolBuilder.Create("test", "desc", (_, ct) => ValueTask.FromResult("ok"), schema);
        Assert.Equal("object", tool.ProtocolTool.InputSchema.GetProperty("type").GetString());
    }

    [Fact]
    public void Create_WithoutSchema_DefaultsEmptyObject()
    {
        var tool = DelegateToolBuilder.Create("test", "desc", (_, ct) => ValueTask.FromResult("ok"));
        Assert.Equal("object", tool.ProtocolTool.InputSchema.GetProperty("type").GetString());
        Assert.False(tool.ProtocolTool.InputSchema.GetProperty("additionalProperties").GetBoolean());
    }

    [Fact]
    public void Create_MetadataDefaultsEmpty()
    {
        var tool = DelegateToolBuilder.Create("test", "desc", (_, ct) => ValueTask.FromResult("ok"));
        Assert.Empty(tool.Metadata);
    }

    [Fact]
    public void AddMcpifyTool_RegistersAsMcpServerTool()
    {
        var services = new ServiceCollection();
        services.AddMcpifyTool("custom", "A custom tool", (_, ct) => ValueTask.FromResult("ok"));

        var provider = services.BuildServiceProvider();
        var tools = provider.GetServices<McpServerTool>().ToList();
        Assert.Single(tools);
        Assert.Equal("custom", tools[0].ProtocolTool.Name);
    }

    [Fact]
    public void AddMcpifyTool_OverloadWithStringHandler_Registers()
    {
        var services = new ServiceCollection();
        services.AddMcpifyTool("simple", "Simple tool", (args, ct) => ValueTask.FromResult("result"));

        var provider = services.BuildServiceProvider();
        var tools = provider.GetServices<McpServerTool>().ToList();
        Assert.Single(tools);
        Assert.Equal("simple", tools[0].ProtocolTool.Name);
    }
}