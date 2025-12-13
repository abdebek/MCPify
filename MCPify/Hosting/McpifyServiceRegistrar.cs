using MCPify.Core;
using MCPify.Endpoints;
using MCPify.OpenApi;
using MCPify.Schema;
using MCPify.Tools;
using MCPify.Core.Auth;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.IO;

namespace MCPify.Hosting;

public class McpifyServiceRegistrar
{
    private readonly IServiceProvider _serviceProvider;
    private readonly McpifyOptions _options;
    private readonly IJsonSchemaGenerator _schema;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<McpifyServiceRegistrar> _logger;
    private readonly IOpenApiProvider _openApiProvider;
    private readonly OpenApiOAuthParser _oauthParser;
    private readonly OAuthConfigurationStore _oauthStore;

    public McpifyServiceRegistrar(
        IServiceProvider serviceProvider,
        McpifyOptions options,
        IJsonSchemaGenerator schema,
        IHttpClientFactory httpClientFactory,
        ILogger<McpifyServiceRegistrar> logger,
        IOpenApiProvider openApiProvider,
        OpenApiOAuthParser oauthParser,
        OAuthConfigurationStore oauthStore)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _schema = schema;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _openApiProvider = openApiProvider;
        _oauthParser = oauthParser;
        _oauthStore = oauthStore;
    }

    public async Task RegisterToolsAsync(IEnumerable<EndpointDataSource>? endpointDataSources = null)
    {
        await RegisterExternalEndpointsAsync();

        if (_options.LocalEndpoints?.Enabled == true)
        {
            try
            {
                RegisterLocalEndpoints(endpointDataSources);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[MCPify] Error initializing local endpoints");
            }
        }
    }

    private async Task RegisterExternalEndpointsAsync()
    {
        if (_options.ExternalApis.Count == 0) return;

        var toolCollection = _serviceProvider.GetService<McpServerPrimitiveCollection<McpServerTool>>();
        if (toolCollection == null)
        {
             _logger.LogWarning("[MCPify] McpServerPrimitiveCollection not found. External tools cannot be registered.");
             return;
        }

        foreach (var apiOptions in _options.ExternalApis)
        {
            var source = apiOptions.SwaggerFilePath ?? apiOptions.SwaggerUrl;
            if (string.IsNullOrEmpty(source))
            {
                _logger.LogWarning("[MCPify] ExternalApiOptions requires either SwaggerUrl or SwaggerFilePath");
                continue;
            }

            try
            {
                var document = await _openApiProvider.LoadAsync(source);
                
                var oauthConfig = _oauthParser.Parse(document);
                if (oauthConfig != null)
                {
                    _oauthStore.AddConfiguration(oauthConfig);
                    _logger.LogInformation("[MCPify] Discovered OAuth configuration in {Source}", source);
                }

                var operations = _openApiProvider.GetOperations(document);

                if (apiOptions.Filter != null)
                {
                    operations = operations.Where(apiOptions.Filter);
                }

                var httpClient = _httpClientFactory.CreateClient();

                var count = 0;
                foreach (var operation in operations)
                {
                    var toolName = string.IsNullOrEmpty(apiOptions.ToolPrefix)
                        ? operation.Name
                        : apiOptions.ToolPrefix + operation.Name;

                    if (toolCollection.Any(t => t.ProtocolTool.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase)))
                    {
                        _logger.LogDebug("[MCPify] Skipping duplicate external tool registration for {ToolName}.", toolName);
                        continue;
                    }

                    var descriptor = operation with { Name = toolName };

                    var apiOpts = new McpifyOptions
                    {
                        DefaultHeaders = new Dictionary<string, string>(_options.DefaultHeaders)
                    };

                    foreach (var header in apiOptions.DefaultHeaders)
                    {
                        apiOpts.DefaultHeaders[header.Key] = header.Value;
                    }

                    var tool = new OpenApiProxyTool(descriptor, apiOptions.ApiBaseUrl, httpClient, _schema, apiOpts, apiOptions.AuthenticationFactory);
                    toolCollection.Add(tool);
                    count++;
                }

                _logger.LogInformation("[MCPify] Successfully registered {Count} tools from {Source}.", count, source);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[MCPify] Failed to load OpenAPI spec from {Source}. Error: {ErrorMessage}", source, ex.Message);
            }
        }
    }

    private void RegisterLocalEndpoints(IEnumerable<EndpointDataSource>? endpointDataSources)
    {
        var endpointProvider = _serviceProvider.GetRequiredService<IEndpointMetadataProvider>() as AspNetCoreEndpointMetadataProvider;
        if (endpointProvider == null) return;

        var toolCollection = _serviceProvider.GetService<McpServerPrimitiveCollection<McpServerTool>>();
        if (toolCollection == null)
        {
             _logger.LogWarning("[MCPify] McpServerPrimitiveCollection not found. Local endpoints cannot be registered.");
             return;
        }

        var operations = endpointProvider.GetLocalEndpoints(endpointDataSources);

        if (_options.LocalEndpoints!.Filter != null)
        {
            operations = operations.Where(_options.LocalEndpoints.Filter);
        }

        var httpClient = _httpClientFactory.CreateClient();

        string BaseUrlProvider()
        {
            var server = _serviceProvider.GetService<IServer>();
            var addresses = server?.Features.Get<IServerAddressesFeature>()?.Addresses;
            return _options.LocalEndpoints?.BaseUrlOverride
                   ?? addresses?.FirstOrDefault()
                   ?? Constants.DefaultBaseUrl;
        }

        var count = 0;
        foreach (var operation in operations)
        {
            var toolName = string.IsNullOrEmpty(_options.LocalEndpoints.ToolPrefix)
                ? operation.Name
                : _options.LocalEndpoints.ToolPrefix + operation.Name;

            if (toolCollection.Any(t => t.ProtocolTool.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogDebug("[MCPify] Skipping duplicate local tool registration for {ToolName}.", toolName);
                continue;
            }

            var descriptor = operation with { Name = toolName };

            var localOpts = new McpifyOptions
            {
                DefaultHeaders = _options.LocalEndpoints.DefaultHeaders
            };

            var effectiveAuthFactory = (descriptor.Operation.Security != null && descriptor.Operation.Security.Count > 0)
                ? _options.LocalEndpoints.AuthenticationFactory
                : null;

            var tool = new OpenApiProxyTool(descriptor, BaseUrlProvider, httpClient, _schema, localOpts, effectiveAuthFactory);
            toolCollection.Add(tool);
            count++;
        }

        _logger.LogInformation("[MCPify] Successfully registered {Count} local endpoint tools.", count);
    }
}