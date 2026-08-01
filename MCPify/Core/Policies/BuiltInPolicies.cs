using System.Collections.Concurrent;
using System.Text.Json;
using MCPify.Core;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPify.Core.Policies;

/// <summary>
/// Rate limit policy: allows a max number of tool calls per session per time window.
/// </summary>
public class RateLimitPolicy : IToolInvocationPolicy
{
    private readonly int _maxCalls;
    private readonly TimeSpan _window;
    private readonly ConcurrentDictionary<string, CallWindow> _windows = new();

    public RateLimitPolicy(int maxCalls, TimeSpan window)
    {
        _maxCalls = maxCalls;
        _window = window;
    }

    public ValueTask<CallToolResult?> EvaluateAsync(ToolInvocationContext context, CancellationToken cancellationToken)
    {
        var key = context.SessionId ?? context.ClientId ?? "anonymous";
        var now = DateTime.UtcNow;
        var window = _windows.GetOrAdd(key, _ => new CallWindow());
        lock (window)
        {
            if (now - window.WindowStart > _window)
            {
                window.WindowStart = now;
                window.Count = 0;
            }
            window.Count++;
            if (window.Count > _maxCalls)
            {
                return ValueTask.FromResult<CallToolResult?>(new CallToolResult
                {
                    IsError = true,
                    Content = new[] { new TextContentBlock { Text = $"Rate limit exceeded: {_maxCalls} calls per {_window.TotalSeconds}s. Try again later." } }
                });
            }
        }
        return ValueTask.FromResult<CallToolResult?>(null);
    }

    private class CallWindow
    {
        public DateTime WindowStart { get; set; } = DateTime.UtcNow;
        public int Count { get; set; }
    }
}

/// <summary>
/// Audit logging policy: records every tool invocation to the log.
/// </summary>
public class AuditLogPolicy : IToolInvocationPolicy
{
    private readonly ILogger<AuditLogPolicy>? _logger;

    public AuditLogPolicy(ILogger<AuditLogPolicy>? logger = null) => _logger = logger;

    public ValueTask<CallToolResult?> EvaluateAsync(ToolInvocationContext context, CancellationToken cancellationToken)
    {
        _logger?.LogInformation("Tool audit: tool={ToolName} session={SessionId} client={ClientId}", context.ToolName, context.SessionId, context.ClientId);
        return ValueTask.FromResult<CallToolResult?>(null);
    }
}

/// <summary>
/// Allowlist policy: only permits specified tool names.
/// </summary>
public class ToolAllowlistPolicy : IToolInvocationPolicy
{
    private readonly HashSet<string> _allowed;

    public ToolAllowlistPolicy(IEnumerable<string> allowed)
        => _allowed = new(allowed, StringComparer.OrdinalIgnoreCase);

    public ValueTask<CallToolResult?> EvaluateAsync(ToolInvocationContext context, CancellationToken cancellationToken)
    {
        if (_allowed.Contains(context.ToolName))
            return ValueTask.FromResult<CallToolResult?>(null);

        return ValueTask.FromResult<CallToolResult?>(new CallToolResult
        {
            IsError = true,
            Content = new[] { new TextContentBlock { Text = $"Tool '{context.ToolName}' is not in the allowlist." } }
        });
    }
}