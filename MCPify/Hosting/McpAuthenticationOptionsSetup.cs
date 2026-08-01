using System;
using System.Collections.Generic;
using System.Linq;
using MCPify.Core;
using MCPify.Core.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.AspNetCore.Authentication;
using ModelContextProtocol.Authentication;

namespace MCPify.Hosting;

internal sealed class McpAuthenticationOptionsSetup : IConfigureNamedOptions<McpAuthenticationOptions>
{
    public void Configure(McpAuthenticationOptions options)
        => Configure(McpAuthenticationDefaults.AuthenticationScheme, options);

    public void Configure(string? name, McpAuthenticationOptions options)
    {
        var previousHandler = options.Events.OnResourceMetadataRequest;

        options.Events.OnResourceMetadataRequest = async context =>
        {
            if (previousHandler != null)
            {
                await previousHandler(context);
            }

            if (context.ResourceMetadata is null)
            {
                context.ResourceMetadata = BuildMetadata(context.HttpContext);
            }
        };
    }

    private static ProtectedResourceMetadata? BuildMetadata(HttpContext httpContext)
    {
        var services = httpContext.RequestServices;
        var options = services.GetService<McpifyOptions>();
        var oauthStore = services.GetService<OAuthConfigurationStore>();

        if (options is null && oauthStore is null)
        {
            return null;
        }

        var metadata = new ProtectedResourceMetadata
        {
            Resource = ResolveResourceString(options?.ResourceUrlOverride, httpContext),
        };

        if (oauthStore != null)
        {
            PopulateAuthorizationMetadata(metadata, oauthStore);
        }

        return metadata;
    }

    private static string ResolveResourceString(string? overrideUrl, HttpContext httpContext)
    {
        if (!string.IsNullOrWhiteSpace(overrideUrl))
        {
            return overrideUrl.TrimEnd('/');
        }

        return $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
    }

    private static void PopulateAuthorizationMetadata(ProtectedResourceMetadata metadata, OAuthConfigurationStore store)
    {
        var authorizationServers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var configuration in store.GetConfigurations())
        {
            foreach (var scope in configuration.Scopes.Keys)
            {
                scopes.Add(scope);
            }

            if (configuration.AuthorizationServers.Count > 0)
            {
                foreach (var server in configuration.AuthorizationServers)
                {
                    if (!string.IsNullOrWhiteSpace(server) && Uri.TryCreate(server, UriKind.Absolute, out var serverUri))
                    {
                        authorizationServers.Add(serverUri.ToString());
                    }
                }

                continue;
            }

            if (!string.IsNullOrWhiteSpace(configuration.AuthorizationUrl) && Uri.TryCreate(configuration.AuthorizationUrl, UriKind.Absolute, out var authorizationUri))
            {
                authorizationServers.Add(new Uri(authorizationUri.GetLeftPart(UriPartial.Authority)).ToString());
            }
        }

        metadata.AuthorizationServers = authorizationServers.ToList();

        metadata.ScopesSupported = scopes.Count > 0
            ? scopes.ToList()
            : new List<string>();
    }
}
