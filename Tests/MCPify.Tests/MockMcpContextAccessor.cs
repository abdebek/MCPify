using MCPify.Core;

namespace MCPify.Tests;

public class MockMcpContextAccessor : IMcpContextAccessor
{
    public string? SessionId { get; set; } = "test-session";
    public string? ConnectionId { get; set; }
    public string? AccessToken { get; set; }
}