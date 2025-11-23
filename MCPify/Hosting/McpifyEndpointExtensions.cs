using Microsoft.AspNetCore.Builder;
using ModelContextProtocol.AspNetCore;

namespace MCPify.Hosting;

public static class McpifyEndpointExtensions
{
    public static WebApplication MapMcpifyEndpoint(
        this WebApplication app,
        string path = "")
    {
        app.MapMcp(path);

        return app;
    }
}
