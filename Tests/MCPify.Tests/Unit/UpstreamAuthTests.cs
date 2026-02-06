using MCPify.Core;
using MCPify.Core.Auth;
using MCPify.Core.Auth.TokenProviders;
using Microsoft.Extensions.DependencyInjection;

namespace MCPify.Tests.Unit;

public class UpstreamAuthTests
{
    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMcpContextAccessor>(new MockMcpContextAccessor());
        return services.BuildServiceProvider();
    }

    [Fact]
    public void None_Builds_NoTokenProvider()
    {
        var auth = UpstreamAuth.None();
        var provider = auth.Build(BuildServiceProvider());

        Assert.IsType<NoTokenProvider>(provider);
    }

    [Fact]
    public void PassThrough_Builds_McpContextTokenProvider()
    {
        var auth = UpstreamAuth.PassThrough();
        var provider = auth.Build(BuildServiceProvider());

        Assert.IsType<McpContextTokenProvider>(provider);
    }

    [Fact]
    public void ServerManaged_Builds_AuthenticationFactoryTokenProvider()
    {
        var auth = UpstreamAuth.ServerManaged(sp => new BearerAuthentication("test-token"));
        var provider = auth.Build(BuildServiceProvider());

        Assert.IsType<AuthenticationFactoryTokenProvider>(provider);
    }

    [Fact]
    public void Fallback_Builds_CompositeTokenProvider()
    {
        var auth = UpstreamAuth.Fallback(
            UpstreamAuth.PassThrough(),
            UpstreamAuth.None());
        var provider = auth.Build(BuildServiceProvider());

        Assert.IsType<CompositeTokenProvider>(provider);
    }

    [Fact]
    public void Custom_Builds_UserProvidedProvider()
    {
        var auth = UpstreamAuth.Custom(sp => NoTokenProvider.Instance);
        var provider = auth.Build(BuildServiceProvider());

        Assert.IsType<NoTokenProvider>(provider);
    }

    [Fact]
    public void ServerManaged_ThrowsOnNullFactory()
    {
        Assert.Throws<ArgumentNullException>(() =>
            UpstreamAuth.ServerManaged(null!));
    }

    [Fact]
    public void Fallback_ThrowsOnNullArray()
    {
        Assert.Throws<ArgumentNullException>(() =>
            UpstreamAuth.Fallback(null!));
    }

    [Fact]
    public void Fallback_ThrowsOnSingleStrategy()
    {
        Assert.Throws<ArgumentException>(() =>
            UpstreamAuth.Fallback(UpstreamAuth.None()));
    }

    [Fact]
    public void Custom_ThrowsOnNullFactory()
    {
        Assert.Throws<ArgumentNullException>(() =>
            UpstreamAuth.Custom(null!));
    }

    [Fact]
    public void TokenExchange_ThrowsOnNullConfigure()
    {
        Assert.Throws<ArgumentNullException>(() =>
            UpstreamAuth.TokenExchange(null!));
    }

    [Fact]
    public void TokenExchange_ThrowsOnMissingTokenEndpoint()
    {
        var sp = BuildServiceProvider();
        var auth = UpstreamAuth.TokenExchange(opts =>
        {
            opts.ClientId = "client";
            // TokenEndpoint intentionally missing
        });

        Assert.Throws<InvalidOperationException>(() => auth.Build(sp));
    }

    [Fact]
    public void TokenExchange_ThrowsOnMissingClientId()
    {
        var sp = BuildServiceProvider();
        var auth = UpstreamAuth.TokenExchange(opts =>
        {
            opts.TokenEndpoint = "https://auth.example.com/token";
            // ClientId intentionally missing
        });

        Assert.Throws<InvalidOperationException>(() => auth.Build(sp));
    }

    [Fact]
    public void TokenExchange_Builds_AuthenticationFactoryTokenProvider_WhenConfigured()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMcpContextAccessor>(new MockMcpContextAccessor());
        services.AddSingleton<ISecureTokenStore>(new InMemoryTokenStore());
        var sp = services.BuildServiceProvider();

        var auth = UpstreamAuth.TokenExchange(opts =>
        {
            opts.TokenEndpoint = "https://auth.example.com/token";
            opts.ClientId = "my-client";
        });

        var provider = auth.Build(sp);
        Assert.IsType<AuthenticationFactoryTokenProvider>(provider);
    }
}
