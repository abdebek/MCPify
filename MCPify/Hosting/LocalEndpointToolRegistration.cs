using MCPify.Core;

namespace MCPify.Hosting;

internal class LocalEndpointToolRegistration
{
    public LocalEndpointsOptions Options { get; }

    public LocalEndpointToolRegistration(LocalEndpointsOptions options)
    {
        Options = options;
    }
}
