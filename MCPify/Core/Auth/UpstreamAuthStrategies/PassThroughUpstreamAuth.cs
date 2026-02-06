using MCPify.Core.Auth.TokenProviders;

namespace MCPify.Core.Auth.UpstreamAuthStrategies;

/// <summary>
/// Forwards the MCP client's access token to the upstream API.
/// </summary>
internal sealed class PassThroughUpstreamAuth : UpstreamAuth
{
    internal override ITokenProvider Build(IServiceProvider serviceProvider)
    {
        return new McpContextTokenProvider(
            serviceProvider.GetRequiredService<IMcpContextAccessor>());
    }
}
