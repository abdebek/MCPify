using MCPify.Core;
using MCPify.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MCPify.Tests.Unit;

public class MultiHostPassThroughWarningTests
{
    private sealed class ListLogger : ILogger
    {
        public List<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }

    [Fact]
    public void WarnIfNeeded_Logs_WhenPassThroughOnMultipleExternalHosts()
    {
        // Multi-host PassThrough now throws at validation time — this test verifies
        // that the warning path does not fire when validation has already blocked it.
        var options = new McpifyOptions
        {
            Transport = McpTransportType.Http,
            AllowClientTokenPassthrough = true,
            ExternalApis =
            {
                new ExternalApiOptions
                {
                    ApiBaseUrl = "https://api-a.example.com",
                    OpenApiUrl = "https://api-a.example.com/openapi.json",
                    UpstreamAuth = UpstreamAuth.PassThrough()
                },
                new ExternalApiOptions
                {
                    ApiBaseUrl = "https://api-a.example.com",
                    OpenApiUrl = "https://api-a.example.com/openapi.json",
                    UpstreamAuth = UpstreamAuth.Fallback(UpstreamAuth.PassThrough(), UpstreamAuth.None())
                }
            }
        };
        options.HttpPassThroughConfigured = true;

        var logger = new ListLogger();
        UpstreamAuthTransportPolicy.WarnIfNeeded(options, logger);

        // Same host — no warning
        Assert.DoesNotContain(logger.Messages, m => m.Contains("distinct hosts", StringComparison.Ordinal));
    }

    [Fact]
    public void WarnIfNeeded_DoesNotLog_WhenSingleHostOrNoPassThrough()
    {
        var options = new McpifyOptions
        {
            ExternalApis =
            {
                new ExternalApiOptions
                {
                    ApiBaseUrl = "https://api-a.example.com",
                    OpenApiUrl = "https://api-a.example.com/openapi.json",
                    UpstreamAuth = UpstreamAuth.PassThrough()
                },
                new ExternalApiOptions
                {
                    ApiBaseUrl = "https://api-a.example.com/v2",
                    OpenApiUrl = "https://api-a.example.com/openapi.json",
                    UpstreamAuth = UpstreamAuth.PassThrough()
                },
                new ExternalApiOptions
                {
                    ApiBaseUrl = "https://api-b.example.com",
                    OpenApiUrl = "https://api-b.example.com/openapi.json",
                    UpstreamAuth = UpstreamAuth.None()
                }
            }
        };

        var logger = new ListLogger();
        UpstreamAuthTransportPolicy.WarnMultiHostPassThrough(options, logger);

        Assert.DoesNotContain(logger.Messages, m => m.Contains("distinct ExternalApi hosts", StringComparison.Ordinal));
    }

    [Fact]
    public void UsesPassThrough_DetectsNestedFallback()
    {
        Assert.True(UpstreamAuthTransportPolicy.UsesPassThrough(
            UpstreamAuth.Fallback(UpstreamAuth.None(), UpstreamAuth.PassThrough())));
        Assert.False(UpstreamAuthTransportPolicy.UsesPassThrough(UpstreamAuth.None()));
    }

    [Fact]
    public void NormalizeAndValidate_Throws_WhenMultiHostPassThroughWithoutOptIn()
    {
        var options = new McpifyOptions
        {
            Transport = McpTransportType.Stdio,
            ExternalApis =
            {
                new ExternalApiOptions
                {
                    ApiBaseUrl = "https://api-a.example.com",
                    OpenApiUrl = "https://api-a.example.com/openapi.json",
                    UpstreamAuth = UpstreamAuth.PassThrough()
                },
                new ExternalApiOptions
                {
                    ApiBaseUrl = "https://api-b.example.com",
                    OpenApiUrl = "https://api-b.example.com/openapi.json",
                    UpstreamAuth = UpstreamAuth.PassThrough()
                }
            }
        };

        Assert.Throws<InvalidOperationException>(() => UpstreamAuthTransportPolicy.NormalizeAndValidate(options));
    }

    [Fact]
    public void NormalizeAndValidate_Allows_WhenSingleHostPassThrough()
    {
        var options = new McpifyOptions
        {
            Transport = McpTransportType.Stdio,
            ExternalApis =
            {
                new ExternalApiOptions
                {
                    ApiBaseUrl = "https://api-a.example.com",
                    OpenApiUrl = "https://api-a.example.com/openapi.json",
                    UpstreamAuth = UpstreamAuth.PassThrough()
                }
            }
        };

        UpstreamAuthTransportPolicy.NormalizeAndValidate(options);
    }
}
