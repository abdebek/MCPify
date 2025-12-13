using MCPify.Core;
using MCPify.Core.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPify.Hosting;

public class McpOAuthAuthenticationMiddleware
{
    private readonly RequestDelegate _next;

    public McpOAuthAuthenticationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip check for metadata endpoint and other non-MCP endpoints
        var path = context.Request.Path;
        if (path.StartsWithSegments("/.well-known") || 
            path.StartsWithSegments("/swagger") || 
            path.StartsWithSegments("/health") ||
            path.StartsWithSegments("/connect") || // OpenIddict or Auth endpoints
            path.StartsWithSegments("/auth"))      // Callback paths
        {
            await _next(context);
            return;
        }

        // Check if OAuth is configured
        var oauthStore = context.RequestServices.GetService<OAuthConfigurationStore>();
        var options = context.RequestServices.GetService<McpifyOptions>();
        
        if (oauthStore == null || !oauthStore.GetConfigurations().Any())
        {
            await _next(context);
            return;
        }

        var accessor = context.RequestServices.GetService<IMcpContextAccessor>();

        // Check for Authorization header
        string? authorization = context.Request.Headers[HeaderNames.Authorization];
        if (string.IsNullOrEmpty(authorization) || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            // Challenge
            var resourceUrl = $"{context.Request.Scheme}://{context.Request.Host}";
            if (options?.LocalEndpoints?.BaseUrlOverride != null)
            {
                resourceUrl = options.LocalEndpoints.BaseUrlOverride;
            }

            // Ensure resourceUrl does not end with slash for concatenation consistency, though URLs handle it.
            resourceUrl = resourceUrl.TrimEnd('/');
            var metadataUrl = $"{resourceUrl}/.well-known/oauth-protected-resource";

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers[HeaderNames.WWWAuthenticate] = 
                $"Bearer realm=\"MCPify\", resource=\"{resourceUrl}\", resource_metadata_url=\"{metadataUrl}\"";
            
            return;
        }
        else
        {
            // Token is present, extract it to context
            if (accessor != null)
            {
                var token = authorization.Substring("Bearer ".Length).Trim();
                if (!string.IsNullOrEmpty(token))
                {
                    accessor.AccessToken = token;
                }
            }
        }

        await _next(context);
    }
}