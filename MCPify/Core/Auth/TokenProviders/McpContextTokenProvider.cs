namespace MCPify.Core.Auth.TokenProviders;

/// <summary>
/// Token provider that retrieves tokens from the MCP context.
/// Used when the MCP client manages authentication and provides tokens.
/// </summary>
public class McpContextTokenProvider : ITokenProvider
{
    private readonly IMcpContextAccessor _mcpContextAccessor;

    public McpContextTokenProvider(IMcpContextAccessor mcpContextAccessor)
    {
        _mcpContextAccessor = mcpContextAccessor;
    }

    public Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        var token = _mcpContextAccessor.AccessToken;

        if (!string.IsNullOrEmpty(token) && !token.Contains(' '))
        {
            token = $"Bearer {token}";
        }

        return Task.FromResult(token);
    }
}
