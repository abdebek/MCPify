using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.AspNetCore;

namespace MCPify.Hosting;

public static class McpifyEndpointExtensions
{
    public static WebApplication MapMcpifyEndpoint(
        this WebApplication app,
        string path = "")
    {
        foreach (var init in app.Services.GetServices<IMcpifyInitializer>())
        {
            init.Initialize();
        }

        app.MapMcp(path);

        return app;
    }
}
