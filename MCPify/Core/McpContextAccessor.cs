namespace MCPify.Core;

public interface IMcpContextAccessor
{
    string? SessionId { get; set; }
    string? ConnectionId { get; set; }
    string? AccessToken { get; set; }
}

/// <summary>
/// Uses <see cref="AsyncLocal{T}"/> so values set by <c>SessionAwareToolDecorator</c>
/// flow through the async context to token providers resolved from any DI scope.
/// Same pattern as ASP.NET Core's <c>HttpContextAccessor</c>.
/// </summary>
public class McpContextAccessor : IMcpContextAccessor
{
    private static readonly AsyncLocal<ContextHolder> _holder = new();

    public string? SessionId
    {
        get => _holder.Value?.SessionId;
        set => EnsureHolder().SessionId = value;
    }

    public string? ConnectionId
    {
        get => _holder.Value?.ConnectionId;
        set => EnsureHolder().ConnectionId = value;
    }

    public string? AccessToken
    {
        get => _holder.Value?.AccessToken;
        set => EnsureHolder().AccessToken = value;
    }

    private static ContextHolder EnsureHolder()
    {
        return _holder.Value ??= new ContextHolder();
    }

    private sealed class ContextHolder
    {
        public string? SessionId;
        public string? ConnectionId;
        public string? AccessToken;
    }
}