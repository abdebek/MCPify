using System.Threading;
using System.Threading.Tasks;

namespace MCPify.Core;

// Define the interface
public interface IMcpContextAccessor
{
    string? SessionId { get; set; }
    string? ConnectionId { get; set; }
    string? AccessToken { get; set; }
}

// Implement the class
public class McpContextAccessor : IMcpContextAccessor
{
    private static readonly System.Threading.AsyncLocal<McpContextHolder> _mcpContextCurrent = new System.Threading.AsyncLocal<McpContextHolder>();

    public string? SessionId
    {
        get => _mcpContextCurrent.Value?.Context?.SessionId;
        set
        {
            var holder = _mcpContextCurrent.Value;
            if (holder != null)
            {
                if (holder.Context == null)
                {
                    holder.Context = new McpContext();
                }
                holder.Context.SessionId = value;
            }
        }
    }

    public string? ConnectionId
    {
        get => _mcpContextCurrent.Value?.Context?.ConnectionId;
        set
        {
            var holder = _mcpContextCurrent.Value;
            if (holder != null)
            {
                if (holder.Context == null)
                {
                    holder.Context = new McpContext();
                }
                holder.Context.ConnectionId = value;
            }
        }
    }

    public string? AccessToken
    {
        get => _mcpContextCurrent.Value?.Context?.AccessToken;
        set
        {
            var holder = _mcpContextCurrent.Value;
            if (holder != null)
            {
                if (holder.Context == null)
                {
                    holder.Context = new McpContext();
                }
                holder.Context.AccessToken = value;
            }
        }
    }

    internal static McpContext? CurrentContext
    {
        get => _mcpContextCurrent.Value?.Context;
        set
        {
            var holder = _mcpContextCurrent.Value;
            if (holder != null)
            {
                holder.Context = value;
            }
            else
            {
                _mcpContextCurrent.Value = new McpContextHolder { Context = value };
            }
        }
    }

    // Fix for CS0053: Make McpContext internal
    public class McpContext
    {
        public string? SessionId { get; set; }
        public string? ConnectionId { get; set; }
        public string? AccessToken { get; set; }
    }

    private class McpContextHolder
    {
        public McpContext? Context;
    }
}