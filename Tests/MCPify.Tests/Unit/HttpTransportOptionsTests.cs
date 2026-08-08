using MCPify.Core;
using MCPify.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

namespace MCPify.Tests.Unit;

public class HttpTransportOptionsTests
{
    [Fact]
    public void AddMcpifyCore_AppliesHttpStateless_WhenConfigured()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMcpifyCore(o =>
        {
            o.Transport = McpTransportType.Http;
            o.HttpStateless = false;
        });

        using var provider = services.BuildServiceProvider();
        var httpOpts = provider.GetRequiredService<IOptions<HttpServerTransportOptions>>().Value;

        Assert.False(httpOpts.Stateless);
    }

    [Fact]
    public void AddMcpifyCore_InvokesConfigureHttpTransport()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var configured = false;
        services.AddMcpifyCore(o =>
        {
            o.Transport = McpTransportType.Http;
            // HttpStateless is applied first; ConfigureHttpTransport can still override.
            o.HttpStateless = true;
            o.ConfigureHttpTransport = opts =>
            {
                configured = true;
                opts.Stateless = false;
            };
        });

        using var provider = services.BuildServiceProvider();
        var httpOpts = provider.GetRequiredService<IOptions<HttpServerTransportOptions>>().Value;

        Assert.True(configured);
        Assert.False(httpOpts.Stateless);
    }
}
