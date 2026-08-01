using MCPify.Core;
using MCPify.Endpoints;
using MCPify.OpenApi;
using MCPify.Schema;
using MCPify.Tools;
using MCPify.Core.Auth;
using MCPify.Core.Auth.TokenProviders;
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
    private readonly OpenApiOAuthParser? _oauthParser;
    private readonly OAuthConfigurationStore? _oauthStore;

    public McpifyServiceRegistrar(
        IServiceProvider serviceProvider,
        McpifyOptions options,
        IJsonSchemaGenerator schema,
        IHttpClientFactory httpClientFactory,
        ILogger<McpifyServiceRegistrar> logger,
        IOpenApiProvider openApiProvider,
        OpenApiOAuthParser? oauthParser = null,
        OAuthConfigurationStore? oauthStore = null)
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
        UpstreamAuthTransportPolicy.WarnIfNeeded(_options, _logger);

        // 1. Register manual tools from DI
        var toolCollection = _serviceProvider.GetService<McpServerPrimitiveCollection<McpServerTool>>();
        if (toolCollection != null)
        {
            var manualTools = _serviceProvider.GetServices<McpServerTool>();
            foreach (var tool in manualTools)
            {
                if (!toolCollection.Any(t => t.ProtocolTool.Name.Equals(tool.ProtocolTool.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    // Wrap with Session Decorator to ensure context in Stdio/Non-HTTP scenarios
                    var decoratedTool = new SessionAwareToolDecorator(tool, _serviceProvider);
                    toolCollection.Add(decoratedTool);
                    _logger.LogDebug("[MCPify] Registered manual tool: {ToolName}", tool.ProtocolTool.Name);
                }
            }
        }

        // 2. Register external APIs
        await RegisterExternalEndpointsAsync();

        // 3. Register local endpoints
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
            var source = apiOptions.OpenApiFilePath ?? apiOptions.OpenApiUrl;
            if (string.IsNullOrEmpty(source))
            {
                _logger.LogWarning("[MCPify] ExternalApiOptions requires either OpenApiUrl or OpenApiFilePath");
                continue;
            }

            try
            {
                var document = await _openApiProvider.LoadAsync(source);

                // Only parse OAuth if auth services are registered
                if (_oauthParser is not null && _oauthStore is not null)
                {
                    var oauthConfig = _oauthParser.Parse(document);
                    if (oauthConfig != null)
                    {
                        _oauthStore.AddConfiguration(oauthConfig);
                        _logger.LogInformation("[MCPify] Discovered OAuth configuration in {Source}", source);
                    }
                }

                var operations = _openApiProvider.GetOperations(document);

                operations = ApplyToolFilter(operations, apiOptions.ToolFilter, apiOptions.Filter);

                var perApiLimit = apiOptions.MaxTools > 0 ? apiOptions.MaxTools : int.MaxValue;
                var httpClient = _httpClientFactory.CreateClient();

                var count = 0;
                foreach (var operation in operations)
                {
                    if (count >= perApiLimit)
                    {
                        _logger.LogWarning("[MCPify] Per-API tool limit ({Limit}) reached for {Source}; skipping remaining operations.", perApiLimit, source);
                        break;
                    }

                    if (toolCollection.Count >= _options.MaxTools)
                    {
                        _logger.LogWarning("[MCPify] Global tool limit ({Limit}) reached; skipping remaining operations from {Source}.", _options.MaxTools, source);
                        break;
                    }
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

                    var hasSecurity = descriptor.Operation.Security != null && descriptor.Operation.Security.Count > 0;
                    var effectiveUpstreamAuth = hasSecurity ? apiOptions.UpstreamAuth : UpstreamAuth.None();
                    var tokenProvider = TokenProviderFactory.Create(_serviceProvider, effectiveUpstreamAuth);
                    var tool = new OpenApiProxyTool(descriptor, apiOptions.ApiBaseUrl, httpClient, _schema, apiOpts, tokenProvider);
                    var decoratedTool = new SessionAwareToolDecorator(tool, _serviceProvider);
                    toolCollection.Add(decoratedTool);
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

        operations = ApplyToolFilter(operations, _options.LocalEndpoints!.ToolFilter, _options.LocalEndpoints.Filter);

        var perApiLimit = _options.LocalEndpoints.MaxTools > 0 ? _options.LocalEndpoints.MaxTools : int.MaxValue;
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
            if (count >= perApiLimit)
            {
                _logger.LogWarning("[MCPify] Local endpoint tool limit ({Limit}) reached; skipping remaining.", perApiLimit);
                break;
            }

            if (toolCollection.Count >= _options.MaxTools)
            {
                _logger.LogWarning("[MCPify] Global tool limit ({Limit}) reached; skipping remaining local endpoints.", _options.MaxTools);
                break;
            }
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

            var hasSecurity = descriptor.Operation.Security != null && descriptor.Operation.Security.Count > 0;

            var effectiveUpstreamAuth = hasSecurity ? _options.LocalEndpoints.UpstreamAuth : UpstreamAuth.None();
            var tokenProvider = TokenProviderFactory.Create(_serviceProvider, effectiveUpstreamAuth);
            var tool = new OpenApiProxyTool(descriptor, BaseUrlProvider, httpClient, _schema, localOpts, tokenProvider);
            var decoratedTool = new SessionAwareToolDecorator(tool, _serviceProvider);
            toolCollection.Add(decoratedTool);
            count++;
        }

        _logger.LogInformation("[MCPify] Successfully registered {Count} local endpoint tools.", count);
    }

    private static IEnumerable<OpenApiOperationDescriptor> ApplyToolFilter(
        IEnumerable<OpenApiOperationDescriptor> operations,
        ToolFilter? toolFilter,
        Func<OpenApiOperationDescriptor, bool>? predicate)
    {
        if (toolFilter != null)
            operations = operations.Where(toolFilter.Matches);

        if (predicate != null)
            operations = operations.Where(predicate);

        return operations;
    }
}
