using MCPify.Core;

namespace MCPify.Endpoints;

public interface IEndpointMetadataProvider
{
    IEnumerable<OpenApiOperationDescriptor> GetLocalEndpoints();
}
