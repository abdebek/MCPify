using MCPify.Core;
using MCPify.Core.Policies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using Xunit;

namespace MCPify.Tests;

public class PolicyTests
{
    private static ToolInvocationContext Ctx(string tool, string? session = null) =>
        new(tool, session, null, null, new ServiceCollection().BuildServiceProvider());

    [Fact]
    public async Task RateLimitPolicy_AllowsUnderLimit()
    {
        var policy = new RateLimitPolicy(3, TimeSpan.FromSeconds(60));
        for (var i = 0; i < 3; i++)
        {
            var result = await policy.EvaluateAsync(Ctx("test", "s1"), CancellationToken.None);
            Assert.Null(result);
        }
    }

    [Fact]
    public async Task RateLimitPolicy_BlocksOverLimit()
    {
        var policy = new RateLimitPolicy(2, TimeSpan.FromSeconds(60));
        await policy.EvaluateAsync(Ctx("test", "s1"), CancellationToken.None);
        await policy.EvaluateAsync(Ctx("test", "s1"), CancellationToken.None);
        var result = await policy.EvaluateAsync(Ctx("test", "s1"), CancellationToken.None);
        Assert.NotNull(result);
        Assert.True(result!.IsError);
    }

    [Fact]
    public async Task RateLimitPolicy_TracksPerSession()
    {
        var policy = new RateLimitPolicy(1, TimeSpan.FromSeconds(60));
        var r1 = await policy.EvaluateAsync(Ctx("test", "s1"), CancellationToken.None);
        var r2 = await policy.EvaluateAsync(Ctx("test", "s2"), CancellationToken.None);
        Assert.Null(r1);
        Assert.Null(r2);
        var blocked = await policy.EvaluateAsync(Ctx("test", "s1"), CancellationToken.None);
        Assert.NotNull(blocked);
    }

    [Fact]
    public async Task ToolAllowlistPolicy_AllowsListedTools()
    {
        var policy = new ToolAllowlistPolicy(new[] { "allowed_tool" });
        var ok = await policy.EvaluateAsync(Ctx("allowed_tool"), CancellationToken.None);
        Assert.Null(ok);
    }

    [Fact]
    public async Task ToolAllowlistPolicy_BlocksUnlistedTools()
    {
        var policy = new ToolAllowlistPolicy(new[] { "allowed_tool" });
        var blocked = await policy.EvaluateAsync(Ctx("blocked_tool"), CancellationToken.None);
        Assert.NotNull(blocked);
        Assert.True(blocked!.IsError);
    }

    [Fact]
    public async Task AuditLogPolicy_AlwaysAllows()
    {
        var logger = new LoggerFactory().CreateLogger<AuditLogPolicy>();
        var policy = new AuditLogPolicy(logger);
        var result = await policy.EvaluateAsync(Ctx("any_tool", "s1"), CancellationToken.None);
        Assert.Null(result);
    }

    [Fact]
    public async Task Policies_RegisteredViaDI_AreEvaluated()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IToolInvocationPolicy, ToolAllowlistPolicy>(_ => new ToolAllowlistPolicy(new[] { "ok" }));

        var sp = services.BuildServiceProvider();
        var policies = sp.GetService<IEnumerable<IToolInvocationPolicy>>()?.ToList();
        Assert.NotNull(policies);
        Assert.Single(policies!);

        var blocked = await policies![0].EvaluateAsync(Ctx("nope"), CancellationToken.None);
        Assert.NotNull(blocked);
    }
}