using MCPify.Core.Auth.TokenProviders;

namespace MCPify.Core.Auth.UpstreamAuthStrategies;

/// <summary>
/// No authentication — upstream API is public.
/// </summary>
internal sealed class NoneUpstreamAuth : UpstreamAuth
{
    internal override ITokenProvider Build(IServiceProvider serviceProvider)
    {
        return NoTokenProvider.Instance;
    }
}
