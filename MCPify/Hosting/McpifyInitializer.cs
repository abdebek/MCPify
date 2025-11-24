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

    public async Task InitializeAsync()
    {
        var provider = _serviceProvider.GetRequiredService<IOpenApiProvider>();
        var schema = _serviceProvider.GetRequiredService<IJsonSchemaGenerator>();
        var httpClient = _serviceProvider.GetRequiredService<HttpClient>();

        var primitives = _serviceProvider.GetService<McpServerPrimitiveCollection<McpServerTool>>();

        if (primitives == null)
        {
            return;
        }

        var document = await provider.LoadAsync(_swaggerUrl);
        var operations = provider.GetOperations(document);

        if (_options.Filter != null)
        {
            operations = operations.Where(_options.Filter);
        }

        foreach (var operation in operations)
        {
            var toolName = string.IsNullOrEmpty(_options.ToolPrefix)
                ? operation.Name
                : _options.ToolPrefix + operation.Name;

            var descriptor = operation with { Name = toolName };
            var tool = new OpenApiProxyTool(descriptor, _apiBaseUrl, httpClient, schema, _options);

            primitives.Add(tool);
        }
    }
}