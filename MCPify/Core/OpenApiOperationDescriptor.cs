using Microsoft.OpenApi.Models;

namespace MCPify.Core;

public record OpenApiOperationDescriptor(
    string Name,
    string Route,
    OperationType Method,
    OpenApiOperation Operation
);
