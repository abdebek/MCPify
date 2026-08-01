using Microsoft.Extensions.DependencyInjection;

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

        ValidateMultiHostPassThrough(options);
    }

    private static void ValidateMultiHostPassThrough(McpifyOptions options)
    {
        var hosts = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var api in options.ExternalApis)
        {
            if (api.UpstreamAuth is null || !UsesPassThrough(api.UpstreamAuth))
                continue;
            if (TryGetHost(api.ApiBaseUrl, out var host))
                hosts.Add(host);
        }

        if (hosts.Count >= 2)
        {
            throw new InvalidOperationException(
                $"PassThrough is configured for {hosts.Count} distinct ExternalApi hosts ({string.Join(", ", hosts)}). " +
                "The same MCP client token would be forwarded to all of them. " +
                "Use ServerManaged or TokenExchange with distinct provider names per API instead.");
        }
    }

    internal static void WarnIfNeeded(McpifyOptions options, ILogger logger)
    {
        if (options.Transport == McpTransportType.Http &&
            options.AllowClientTokenPassthrough &&
            options.HttpPassThroughConfigured &&
            !options.HttpPassThroughWarningLogged)
        {
            options.HttpPassThroughWarningLogged = true;
            logger.LogWarning(
                "[MCPify] Client token pass-through is enabled on HTTP transport. " +
                "This is unsafe for hosted or multi-user deployments and should be used only for local/dev scenarios.");
        }

        WarnMultiHostPassThrough(options, logger);
    }

    /// <summary>
    /// Logs when PassThrough is configured for multiple ExternalApi base hosts — the same
    /// MCP client token would be sent to distinct audiences.
    /// </summary>
    internal static void WarnMultiHostPassThrough(McpifyOptions options, ILogger logger)
    {
        if (options.MultiHostPassThroughWarningLogged)
            return;

        var hosts = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var api in options.ExternalApis)
        {
            if (api.UpstreamAuth is null || !UsesPassThrough(api.UpstreamAuth))
                continue;
            if (TryGetHost(api.ApiBaseUrl, out var host))
                hosts.Add(host);
        }

        if (hosts.Count < 2)
            return;

        options.MultiHostPassThroughWarningLogged = true;
        logger.LogWarning(
            "[MCPify] PassThrough is active for {Count} distinct hosts ({Hosts}). " +
            "Ensure they share the same token audience.",
            hosts.Count,
            string.Join(", ", hosts));
    }

    internal static bool UsesPassThrough(UpstreamAuth upstreamAuth)
    {
        return upstreamAuth switch
        {
            PassThroughUpstreamAuth => true,
            FallbackUpstreamAuth fallback => fallback.Strategies.Any(UsesPassThrough),
            _ => false
        };
    }

    private static bool TryGetHost(string? apiBaseUrl, out string host)
    {
        host = "";
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out var uri) ||
            string.IsNullOrEmpty(uri.Host))
        {
            return false;
        }

        host = uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}";
        return true;
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

    private static void NormalizeDefaults(LocalEndpointsOptions options, McpTransportType transport)
    {
        if (options.UpstreamAuth != null)
        {
            return;
        }

        var migrated = MigrateLegacy(options.TokenSource, options.AuthenticationFactory);
        options.UpstreamAuth = migrated ?? CreateTransportDefault(transport);
    }

    private static void NormalizeDefaults(ExternalApiOptions options, McpTransportType transport)
    {
        if (options.UpstreamAuth != null)
        {
            return;
        }

        var migrated = MigrateLegacy(options.TokenSource, options.AuthenticationFactory);
        options.UpstreamAuth = migrated ?? CreateTransportDefault(transport);
    }

    /// <summary>
    /// Maps the obsolete <see cref="TokenSource"/> + <see cref="ExternalApiOptions.AuthenticationFactory"/>
    /// pair to the equivalent <see cref="UpstreamAuth"/> strategy so existing config keeps working.
    /// Returns null when no legacy values are set (caller falls back to the transport default).
    /// </summary>
#pragma warning disable CS0612 // Type or member is obsolete
    private static UpstreamAuth? MigrateLegacy(
        TokenSource tokenSource,
        Func<IServiceProvider, IAuthenticationProvider>? authenticationFactory)
    {
        return tokenSource switch
        {
            TokenSource.None => UpstreamAuth.None(),
            TokenSource.Client => UpstreamAuth.PassThrough(),
            TokenSource.Server => authenticationFactory is not null
                ? UpstreamAuth.ServerManaged(authenticationFactory)
                : null,
            TokenSource.Both => authenticationFactory is not null
                ? UpstreamAuth.Fallback(UpstreamAuth.PassThrough(), UpstreamAuth.ServerManaged(authenticationFactory))
                : UpstreamAuth.PassThrough(),
            _ => null
        };
    }
#pragma warning restore CS0612

    private static bool HasPassThroughFirstSelection(McpifyOptions options)
    {
        if (options.LocalEndpoints is { Enabled: true } localEndpoints &&
            localEndpoints.UpstreamAuth is not null &&
            IsPassThroughFirst(localEndpoints.UpstreamAuth))
        {
            return true;
        }

        return options.ExternalApis.Any(api => api.UpstreamAuth is not null && IsPassThroughFirst(api.UpstreamAuth));
    }

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
