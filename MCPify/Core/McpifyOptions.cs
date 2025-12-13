using MCPify.OpenApi;
using MCPify.Schema;
using MCPify.Core.Auth;

namespace MCPify.Core;

public class McpifyOptions
{
    public LocalEndpointsOptions? LocalEndpoints { get; set; }

    public List<ExternalApiOptions> ExternalApis { get; set; } = new();

    public IOpenApiProvider? ProviderOverride { get; set; }

    public IJsonSchemaGenerator? SchemaGeneratorOverride { get; set; }

    public Dictionary<string, string> DefaultHeaders { get; set; } = new();

    public TimeSpan OpenApiDownloadTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public McpTransportType Transport { get; set; } = McpTransportType.Http;
}

public enum McpTransportType
{
    Http,
    Stdio
}

public class LocalEndpointsOptions
{
    public bool Enabled { get; set; }

    public string? ToolPrefix { get; set; }

    public string? BaseUrlOverride { get; set; }

    public Func<OpenApiOperationDescriptor, bool>? Filter { get; set; }

    public Dictionary<string, string> DefaultHeaders { get; set; } = new();

    public Func<IServiceProvider, IAuthenticationProvider>? AuthenticationFactory { get; set; }
}

public class ExternalApiOptions
{
    public string? SwaggerUrl { get; set; }

    public string? SwaggerFilePath { get; set; }

    public required string ApiBaseUrl { get; set; }

    public string? ToolPrefix { get; set; }

    public Func<OpenApiOperationDescriptor, bool>? Filter { get; set; }

    public Dictionary<string, string> DefaultHeaders { get; set; } = new();

    public Func<IServiceProvider, IAuthenticationProvider>? AuthenticationFactory { get; set; }
}
