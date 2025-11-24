using MCPify.OpenApi;
using MCPify.Schema;

namespace MCPify.Core;

public class McpifyOptions
{
    public string? ToolPrefix { get; set; }

    public Func<OpenApiOperationDescriptor, bool>? Filter { get; set; }

    public IOpenApiProvider? ProviderOverride { get; set; }

    public IJsonSchemaGenerator? SchemaGeneratorOverride { get; set; }

    public Dictionary<string, string> DefaultHeaders { get; set; } = new();
}