using Microsoft.OpenApi.Models;
using MCPify.Core;

namespace MCPify.OpenApi;

public interface IOpenApiProvider
{
    Task<OpenApiDocument> LoadAsync(string source);

    IEnumerable<OpenApiOperationDescriptor> GetOperations(OpenApiDocument doc);
}
