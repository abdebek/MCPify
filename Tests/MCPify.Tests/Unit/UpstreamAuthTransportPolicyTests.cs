using MCPify.Core;
using MCPify.Core.Auth;
using MCPify.Core.Auth.TokenProviders;
using MCPify.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MCPify.Tests.Unit;

public class UpstreamAuthTransportPolicyTests
{
    [Fact]
    public void AddMcpify_Throws_WhenHttpPassThroughConfiguredWithoutOptIn()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddMcpify(options =>
            {
                options.Transport = McpTransportType.Http;
                options.LocalEndpoints = new LocalEndpointsOptions
                {
                    Enabled = true,
                    UpstreamAuth = UpstreamAuth.PassThrough()
                };
            }));

        Assert.Contains("AllowClientTokenPassthrough", exception.Message);
    }

    [Fact]
    public void AddMcpify_DoesNotThrow_WhenHttpPassThroughOptInEnabled()
    {
        var services = new ServiceCollection();

        services.AddMcpify(options =>
        {
            options.Transport = McpTransportType.Http;
            options.AllowClientTokenPassthrough = true;
            options.LocalEndpoints = new LocalEndpointsOptions
            {
                Enabled = true,
                UpstreamAuth = UpstreamAuth.PassThrough()
            };
        });
    }

    [Fact]
    public void AddMcpify_DoesNotThrow_WhenHttpFallbackStartsWithServerManaged()
    {
        var services = new ServiceCollection();

        services.AddMcpify(options =>
        {
            options.Transport = McpTransportType.Http;
            options.LocalEndpoints = new LocalEndpointsOptions
            {
                Enabled = true,
                UpstreamAuth = UpstreamAuth.Fallback(
                    UpstreamAuth.ServerManaged(_ => new BearerAuthentication("server-token")),
                    UpstreamAuth.PassThrough())
            };
        });
    }

    [Fact]
    public void AddMcpify_DefaultsToServerManaged_WhenHttpAndAuthUnset()
    {
        var services = new ServiceCollection();
        services.AddMcpify(options =>
        {
            options.Transport = McpTransportType.Http;
            options.LocalEndpoints = new LocalEndpointsOptions
            {
                Enabled = true
            };
        });

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<McpifyOptions>();
        Assert.NotNull(options.LocalEndpoints?.UpstreamAuth);

        var provider = options.LocalEndpoints!.UpstreamAuth!.Build(serviceProvider);
        Assert.IsType<AuthenticationFactoryTokenProvider>(provider);
    }

    [Fact]
    public void AddMcpify_DefaultsToPassThrough_WhenStdioAndAuthUnset()
    {
        var services = new ServiceCollection();
        services.AddMcpify(options =>
        {
            options.Transport = McpTransportType.Stdio;
            options.LocalEndpoints = new LocalEndpointsOptions
            {
                Enabled = true
            };
        });

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<McpifyOptions>();
        Assert.NotNull(options.LocalEndpoints?.UpstreamAuth);

        var provider = options.LocalEndpoints!.UpstreamAuth!.Build(serviceProvider);
        Assert.IsType<McpContextTokenProvider>(provider);
    }

    [Fact]
    public void AddMcpify_Throws_WhenHttpExternalFallbackStartsWithPassThroughWithoutOptIn()
    {
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddMcpify(options =>
            {
                options.Transport = McpTransportType.Http;
                options.ExternalApis.Add(new ExternalApiOptions
                {
                    ApiBaseUrl = "https://api.example.com",
                    UpstreamAuth = UpstreamAuth.Fallback(
                        UpstreamAuth.PassThrough(),
                        UpstreamAuth.None())
                });
            }));

        Assert.Contains("AllowClientTokenPassthrough", exception.Message);
    }

    [Fact]
    public void AddMcpify_RegistersStartupWarningHostedService()
    {
        var services = new ServiceCollection();

        services.AddMcpify(options =>
        {
            options.Transport = McpTransportType.Http;
            options.LocalEndpoints = new LocalEndpointsOptions
            {
                Enabled = true
            };
        });

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IHostedService) &&
                          descriptor.ImplementationType?.Name == "HttpPassThroughWarningHostedService");
    }

    [Fact]
    public void AddMcpify_MigratesLegacyTokenSourceServer_WithFactory_ToServerManaged()
    {
        var services = new ServiceCollection();
        services.AddMcpify(options =>
        {
            options.Transport = McpTransportType.Http;
            options.LocalEndpoints = new LocalEndpointsOptions
            {
#pragma warning disable CS0612
                Enabled = true,
                TokenSource = TokenSource.Server,
                AuthenticationFactory = _ => new BearerAuthentication("legacy-token")
#pragma warning restore CS0612
            };
        });

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<McpifyOptions>();
        Assert.NotNull(options.LocalEndpoints?.UpstreamAuth);

        var provider = options.LocalEndpoints!.UpstreamAuth!.Build(serviceProvider);
        Assert.IsType<AuthenticationFactoryTokenProvider>(provider);
    }

    [Fact]
    public void AddMcpify_MigratesLegacyTokenSourceClient_ToPassThrough()
    {
        var services = new ServiceCollection();
        services.AddMcpify(options =>
        {
            options.Transport = McpTransportType.Http;
            options.ExternalApis.Add(new ExternalApiOptions
            {
                ApiBaseUrl = "https://api.example.com",
#pragma warning disable CS0612
                TokenSource = TokenSource.Client
#pragma warning restore CS0612
            });
            options.AllowClientTokenPassthrough = true;
        });

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<McpifyOptions>();
        Assert.NotNull(options.ExternalApis[0].UpstreamAuth);

        var provider = options.ExternalApis[0].UpstreamAuth!.Build(serviceProvider);
        Assert.IsType<McpContextTokenProvider>(provider);
    }

    [Fact]
    public void AddMcpify_MigratesLegacyTokenSourceNone_ToNoneUpstreamAuth()
    {
        var services = new ServiceCollection();
        services.AddMcpify(options =>
        {
            options.Transport = McpTransportType.Http;
            options.ExternalApis.Add(new ExternalApiOptions
            {
                ApiBaseUrl = "https://api.example.com",
#pragma warning disable CS0612
                TokenSource = TokenSource.None
#pragma warning restore CS0612
            });
        });

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<McpifyOptions>();
        Assert.NotNull(options.ExternalApis[0].UpstreamAuth);

        var provider = options.ExternalApis[0].UpstreamAuth!.Build(serviceProvider);
        Assert.IsType<NoTokenProvider>(provider);
    }

    [Fact]
    public void AddMcpify_MigratesLegacyTokenSourceBoth_WithFactory_ToFallback()
    {
        var services = new ServiceCollection();
        services.AddMcpify(options =>
        {
            options.Transport = McpTransportType.Http;
            options.ExternalApis.Add(new ExternalApiOptions
            {
                ApiBaseUrl = "https://api.example.com",
#pragma warning disable CS0612
                TokenSource = TokenSource.Both,
                AuthenticationFactory = _ => new BearerAuthentication("server-token")
#pragma warning restore CS0612
            });
            options.AllowClientTokenPassthrough = true;
        });

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<McpifyOptions>();
        Assert.NotNull(options.ExternalApis[0].UpstreamAuth);

        var provider = options.ExternalApis[0].UpstreamAuth!.Build(serviceProvider);
        Assert.IsType<CompositeTokenProvider>(provider);
    }

    [Fact]
    public void AddMcpify_LegacyTokenSourceServer_WithoutFactory_FallsBackToTransportDefault()
    {
        var services = new ServiceCollection();
        services.AddMcpify(options =>
        {
            options.Transport = McpTransportType.Stdio;
            options.LocalEndpoints = new LocalEndpointsOptions
            {
#pragma warning disable CS0612
                Enabled = true,
                TokenSource = TokenSource.Server
                // No AuthenticationFactory set
#pragma warning restore CS0612
            };
        });

        using var serviceProvider = services.BuildServiceProvider();
        var options = serviceProvider.GetRequiredService<McpifyOptions>();
        Assert.NotNull(options.LocalEndpoints?.UpstreamAuth);

        // Stdio default is PassThrough
        var provider = options.LocalEndpoints!.UpstreamAuth!.Build(serviceProvider);
        Assert.IsType<McpContextTokenProvider>(provider);
    }
}
