using MCPify.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MCPify.Hosting;

internal sealed class HttpPassThroughWarningHostedService : IHostedService
{
    private readonly McpifyOptions _options;
    private readonly ILogger<HttpPassThroughWarningHostedService> _logger;

    public HttpPassThroughWarningHostedService(
        McpifyOptions options,
        ILogger<HttpPassThroughWarningHostedService> logger)
    {
        _options = options;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        UpstreamAuthTransportPolicy.WarnIfNeeded(_options, _logger);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
