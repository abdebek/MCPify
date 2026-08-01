using System.Text.Json;
using MCPify.Core.Auth;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MCPify.Tools;

public class DelegateTool : McpServerTool
{
    private readonly string _name;
    private readonly string _description;
    private readonly JsonElement _inputSchema;
    private readonly Func<RequestContext<CallToolRequestParams>, CancellationToken, ValueTask<CallToolResult>> _handler;
    private readonly IReadOnlyList<object> _metadata;

    public DelegateTool(
        string name,
        string description,
        object? inputSchema,
        Func<RequestContext<CallToolRequestParams>, CancellationToken, ValueTask<CallToolResult>> handler,
        IReadOnlyList<object>? metadata = null)
    {
        _name = name;
        _description = description;
        _inputSchema = inputSchema == null
            ? JsonDocument.Parse("""{"type":"object","properties":{},"additionalProperties":false}""").RootElement
            : JsonSerializer.SerializeToElement(inputSchema);
        _handler = handler;
        _metadata = metadata ?? Array.Empty<object>();
    }

    public override Tool ProtocolTool => new()
    {
        Name = _name,
        Description = _description,
        InputSchema = _inputSchema
    };

    public override IReadOnlyList<object> Metadata => _metadata;

    public override ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken)
        => _handler(context, cancellationToken);
}

public static class DelegateToolBuilder
{
    public static DelegateTool Create(
        string name,
        string description,
        Func<RequestContext<CallToolRequestParams>, CancellationToken, ValueTask<CallToolResult>> handler,
        object? inputSchema = null,
        IReadOnlyList<object>? metadata = null)
        => new(name, description, inputSchema, handler, metadata);

    public static DelegateTool Create(
        string name,
        string description,
        Func<JsonElement?, CancellationToken, ValueTask<string>> handler,
        object? inputSchema = null,
        IReadOnlyList<object>? metadata = null)
    {
        return new(name, description, inputSchema, async (ctx, ct) =>
        {
            var args = ctx.Params?.Arguments;
            JsonElement? primary = null;
            if (args != null && args.Count > 0)
                primary = args.FirstOrDefault().Value;

            var result = await handler(primary, ct);
            return new CallToolResult
            {
                Content = new[] { new TextContentBlock { Text = result } }
            };
        }, metadata);
    }
}