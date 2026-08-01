using MCPify.Hosting;
using Xunit;

namespace MCPify.Tests.Unit;

public class WwwAuthenticateScopeTests
{
    [Theory]
    [InlineData("Bearer resource_metadata=\"https://x/.well-known/oauth-protected-resource\"", "read write",
        "Bearer resource_metadata=\"https://x/.well-known/oauth-protected-resource\", scope=\"read write\"")]
    [InlineData("Bearer realm=\"mcp\"", "api", "Bearer realm=\"mcp\", scope=\"api\"")]
    [InlineData("Bearer scope=\"existing\"", "api", "Bearer scope=\"existing\"")]
    [InlineData("Basic realm=\"x\"", "api", "Basic realm=\"x\"")]
    [InlineData(null, "api", "Bearer scope=\"api\"")]
    public void AppendScopeIfBearer_BehavesAsExpected(string? header, string scopes, string expected)
    {
        Assert.Equal(expected, WwwAuthenticateScopeMiddleware.AppendScopeIfBearer(header, scopes));
    }
}
