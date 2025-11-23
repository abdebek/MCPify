using MCPify.Core;
using MCPify.OpenApi;
using MCPify.Schema;
using MCPify.Tools;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace MCPify.Hosting;

internal class McpifyInitializer : IMcpifyInitializer
{
    private readonly IServiceProvider _serviceProvider;
    private readonly string _swaggerUrl;
    private readonly string _apiBaseUrl;
    private readonly McpifyOptions _options;

    public McpifyInitializer(
        IServiceProvider serviceProvider,
        string swaggerUrl,
        string apiBaseUrl,
        McpifyOptions options)
    {
        _serviceProvider = serviceProvider;
        _swaggerUrl = swaggerUrl;
        _apiBaseUrl = apiBaseUrl;
        _options = options;
    }

    public void Initialize()
    {
        var provider = _serviceProvider.GetRequiredService<IOpenApiProvider>();
        var schema = _serviceProvider.GetRequiredService<IJsonSchemaGenerator>();
        var httpClient = _serviceProvider.GetRequiredService<HttpClient>();

        var document = provider.LoadAsync(_swaggerUrl).GetAwaiter().GetResult();
        var operations = provider.GetOperations(document);

        if (_options.Filter != null)
        {
            operations = operations.Where(_options.Filter);
        }

        // Try to get the tools collection, it may or may not be registered
        var primitives = _serviceProvider.GetService<McpServerPrimitiveCollection<McpServerTool>>();

        if (primitives == null)
        {
            // If not registered, we need to register tools directly in the service collection
            // This is handled in the service registration phase
            return;
        }

        foreach (var operation in operations)
        {
            var toolName = string.IsNullOrEmpty(_options.ToolPrefix)
                ? operation.Name
                : _options.ToolPrefix + operation.Name;

            var descriptor = operation with { Name = toolName };
            var tool = new OpenApiProxyTool(descriptor, _apiBaseUrl, httpClient, schema);

            primitives.Add(tool);
        }
    }
}
