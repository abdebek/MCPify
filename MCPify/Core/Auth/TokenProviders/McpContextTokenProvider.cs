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

    public Task<bool> ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        var token = _mcpContextAccessor.AccessToken;

        if (string.IsNullOrEmpty(token))
        {
            return Task.FromResult(false);
        }

        var value = token.Contains(' ') ? token : $"Bearer {token}";
        var parts = value.Split(' ', 2);
        if (parts.Length == 2)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(parts[0], parts[1]);
        }
        else
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        return Task.FromResult(true);
    }
}
