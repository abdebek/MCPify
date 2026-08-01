using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPify.Core;

/// <summary>
/// Called before every tool invocation. Return non-null to short-circuit the call
/// (e.g. rate limit, allowlist denial). Return null to allow the invocation to proceed.
/// </summary>
public interface IToolInvocationPolicy
{
    ValueTask<CallToolResult?> EvaluateAsync(ToolInvocationContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Context for tool invocation policy evaluation.
/// </summary>
public sealed record ToolInvocationContext(
    string ToolName,
    string? SessionId,
    string? ClientId,
    IReadOnlyDictionary<string, JsonElement>? Arguments,
    IServiceProvider Services);