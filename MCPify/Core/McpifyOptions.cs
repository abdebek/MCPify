using MCPify.OpenApi;
using MCPify.Schema;

namespace MCPify.Core;

public class McpifyOptions
{
    public LocalEndpointsOptions? LocalEndpoints { get; set; }

    public List<ExternalApiOptions> ExternalApis { get; set; } = new();

    public IOpenApiProvider? ProviderOverride { get; set; }

    public IJsonSchemaGenerator? SchemaGeneratorOverride { get; set; }

    public Dictionary<string, string> DefaultHeaders { get; set; } = new();
}

public class LocalEndpointsOptions
{
    public bool Enabled { get; set; }

    public string? ToolPrefix { get; set; }

    public Func<OpenApiOperationDescriptor, bool>? Filter { get; set; }

    public Dictionary<string, string> DefaultHeaders { get; set; } = new();
}

public class ExternalApiOptions
{
    public string? SwaggerUrl { get; set; }

    public string? SwaggerFilePath { get; set; }

    public required string ApiBaseUrl { get; set; }

    public string? ToolPrefix { get; set; }

    public Func<OpenApiOperationDescriptor, bool>? Filter { get; set; }

    public Dictionary<string, string> DefaultHeaders { get; set; } = new();
}