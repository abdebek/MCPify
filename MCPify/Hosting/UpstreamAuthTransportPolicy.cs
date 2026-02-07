using MCPify.Core;
using MCPify.Core.Auth;
using MCPify.Core.Auth.OAuth;
using MCPify.Core.Auth.UpstreamAuthStrategies;
using Microsoft.Extensions.Logging;

namespace MCPify.Hosting;

internal static class UpstreamAuthTransportPolicy
{
    internal static void NormalizeAndValidate(McpifyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        NormalizeDefaults(options);

        var usesPassThroughOnHttp = options.Transport == McpTransportType.Http && HasPassThroughFirstSelection(options);
        options.HttpPassThroughConfigured = usesPassThroughOnHttp;

        if (usesPassThroughOnHttp && !options.AllowClientTokenPassthrough)
        {
            throw new InvalidOperationException(
                "Client token pass-through is disabled for HTTP transport. " +
                "Set McpifyOptions.AllowClientTokenPassthrough = true to opt in explicitly. " +
                "Use server-managed upstream auth for hosted or multi-user deployments.");
        }
    }

    internal static void WarnIfNeeded(McpifyOptions options, ILogger logger)
    {
        if (options.Transport != McpTransportType.Http ||
            !options.AllowClientTokenPassthrough ||
            !options.HttpPassThroughConfigured ||
            options.HttpPassThroughWarningLogged)
        {
            return;
        }

        options.HttpPassThroughWarningLogged = true;
        logger.LogWarning(
            "[MCPify] Client token pass-through is enabled on HTTP transport. " +
            "This is unsafe for hosted or multi-user deployments and should be used only for local/dev scenarios.");
    }

    private static void NormalizeDefaults(McpifyOptions options)
    {
        if (options.LocalEndpoints is { } localEndpoints)
        {
            NormalizeDefaults(localEndpoints, options.Transport);
        }

        foreach (var externalApi in options.ExternalApis)
        {
            NormalizeDefaults(externalApi, options.Transport);
        }
    }

#pragma warning disable CS0618
    private static void NormalizeDefaults(LocalEndpointsOptions options, McpTransportType transport)
    {
        if (options.UpstreamAuth != null || options.AuthenticationFactory != null || options.TokenSource != TokenSource.Both)
        {
            return;
        }

        options.UpstreamAuth = CreateTransportDefault(transport);
    }

    private static void NormalizeDefaults(ExternalApiOptions options, McpTransportType transport)
    {
        if (options.UpstreamAuth != null || options.AuthenticationFactory != null || options.TokenSource != TokenSource.Both)
        {
            return;
        }

        options.UpstreamAuth = CreateTransportDefault(transport);
    }

    private static bool HasPassThroughFirstSelection(McpifyOptions options)
    {
        if (options.LocalEndpoints is { Enabled: true } localEndpoints &&
            UsesPassThroughFirst(localEndpoints.UpstreamAuth, localEndpoints.TokenSource))
        {
            return true;
        }

        return options.ExternalApis.Any(api => UsesPassThroughFirst(api.UpstreamAuth, api.TokenSource));
    }

    private static bool UsesPassThroughFirst(UpstreamAuth? upstreamAuth, TokenSource tokenSource)
    {
        if (upstreamAuth != null)
        {
            return IsPassThroughFirst(upstreamAuth);
        }

        return tokenSource is TokenSource.Client or TokenSource.Both;
    }
#pragma warning restore CS0618

    private static UpstreamAuth CreateTransportDefault(McpTransportType transport)
    {
        if (transport == McpTransportType.Stdio)
        {
            return UpstreamAuth.PassThrough();
        }

        return UpstreamAuth.ServerManaged(ResolveDefaultAuthenticationProvider);
    }

    private static IAuthenticationProvider ResolveDefaultAuthenticationProvider(IServiceProvider serviceProvider)
    {
        var provider = serviceProvider.GetService<IAuthenticationProvider>();
        if (provider != null)
        {
            return provider;
        }

        var oauthProvider = serviceProvider.GetService<OAuthAuthorizationCodeAuthentication>();
        if (oauthProvider != null)
        {
            return oauthProvider;
        }

        throw new InvalidOperationException(
            "No server-managed IAuthenticationProvider is registered for upstream authentication. " +
            "Register one explicitly or configure UpstreamAuth for the endpoint.");
    }

    internal static bool IsPassThroughFirst(UpstreamAuth upstreamAuth)
    {
        return upstreamAuth switch
        {
            PassThroughUpstreamAuth => true,
            FallbackUpstreamAuth fallback => fallback.Strategies.Count > 0 && IsPassThroughFirst(fallback.Strategies[0]),
            _ => false
        };
    }
}
