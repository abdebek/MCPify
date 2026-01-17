using MCPify.Core;
using MCPify.Core.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace MCPify.Hosting;

public class McpOAuthAuthenticationMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>
    /// Key for storing token validation result in HttpContext.Items for downstream use.
    /// </summary>
    public const string TokenValidationResultKey = "McpTokenValidationResult";

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
        var resourceUrl = GetResourceUrl(context, options);

        // Check for Authorization header
        string? authorization = context.Request.Headers[HeaderNames.Authorization];
        if (string.IsNullOrEmpty(authorization) || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            // No token - return 401 challenge
            await WriteChallengeResponse(context, oauthStore, resourceUrl, null, null);
            return;
        }

        // Extract token
        var token = authorization.Substring("Bearer ".Length).Trim();
        if (string.IsNullOrEmpty(token))
        {
            await WriteChallengeResponse(context, oauthStore, resourceUrl, null, null);
            return;
        }

        // Set token on accessor for downstream use
        if (accessor != null)
        {
            accessor.AccessToken = token;
        }

        // Perform token validation if enabled
        var validationOptions = options?.TokenValidation;
        if (validationOptions?.EnableJwtValidation == true)
        {
            var validator = context.RequestServices.GetService<IAccessTokenValidator>();
            if (validator != null)
            {
                var expectedAudience = validationOptions.ValidateAudience
                    ? (validationOptions.ExpectedAudience ?? resourceUrl)
                    : null;

                var validationResult = await validator.ValidateAsync(token, expectedAudience, context.RequestAborted);

                // Store validation result for downstream use
                context.Items[TokenValidationResultKey] = validationResult;

                if (!validationResult.IsValid)
                {
                    // Token is invalid (expired, malformed, wrong audience) - return 401
                    await WriteInvalidTokenResponse(context, oauthStore, resourceUrl,
                        validationResult.ErrorCode ?? "invalid_token",
                        validationResult.ErrorDescription ?? "Token validation failed");
                    return;
                }

                // Validate scopes if enabled
                if (validationOptions.ValidateScopes)
                {
                    var scopeStore = context.RequestServices.GetService<ScopeRequirementStore>();
                    if (scopeStore != null)
                    {
                        // Use default validation (no specific tool name available at middleware level)
                        var scopeResult = scopeStore.ValidateScopesForTool("*", validationResult.Scopes);

                        if (!scopeResult.IsValid)
                        {
                            // Token is valid but lacks required scopes - return 403
                            await WriteInsufficientScopeResponse(context, resourceUrl, scopeResult.MissingScopes);
                            return;
                        }
                    }
                }
            }
        }

        await _next(context);
    }

    private static string GetResourceUrl(HttpContext context, McpifyOptions? options)
    {
        var resourceUrl = options?.ResourceUrlOverride;
        if (string.IsNullOrWhiteSpace(resourceUrl))
        {
            resourceUrl = options?.LocalEndpoints?.BaseUrlOverride;
        }

        if (string.IsNullOrWhiteSpace(resourceUrl))
        {
            resourceUrl = $"{context.Request.Scheme}://{context.Request.Host}";
        }

        return resourceUrl.TrimEnd('/');
    }

    private static async Task WriteChallengeResponse(
        HttpContext context,
        OAuthConfigurationStore oauthStore,
        string resourceUrl,
        string? errorCode,
        string? errorDescription)
    {
        var metadataUrl = $"{resourceUrl}/.well-known/oauth-protected-resource";

        // Collect all scopes from OAuth configurations per MCP spec
        var allScopes = oauthStore.GetConfigurations()
            .SelectMany(c => c.Scopes.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;

        // Build WWW-Authenticate header per MCP Authorization spec
        var wwwAuthenticate = BuildWwwAuthenticateHeader(metadataUrl, allScopes, errorCode, errorDescription);
        context.Response.Headers[HeaderNames.WWWAuthenticate] = wwwAuthenticate;
    }

    private static async Task WriteInvalidTokenResponse(
        HttpContext context,
        OAuthConfigurationStore oauthStore,
        string resourceUrl,
        string errorCode,
        string errorDescription)
    {
        var metadataUrl = $"{resourceUrl}/.well-known/oauth-protected-resource";

        // Collect all scopes from OAuth configurations
        var allScopes = oauthStore.GetConfigurations()
            .SelectMany(c => c.Scopes.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;

        var wwwAuthenticate = BuildWwwAuthenticateHeader(metadataUrl, allScopes, errorCode, errorDescription);
        context.Response.Headers[HeaderNames.WWWAuthenticate] = wwwAuthenticate;
    }

    private static async Task WriteInsufficientScopeResponse(
        HttpContext context,
        string resourceUrl,
        IReadOnlyList<string> requiredScopes)
    {
        var metadataUrl = $"{resourceUrl}/.well-known/oauth-protected-resource";

        context.Response.StatusCode = StatusCodes.Status403Forbidden;

        // Build WWW-Authenticate header for insufficient_scope per RFC 6750 Section 3.1
        var parts = new List<string>
        {
            "Bearer",
            $"error=\"insufficient_scope\"",
            $"error_description=\"The access token does not have the required scope(s)\"",
            $"resource_metadata=\"{metadataUrl}\""
        };

        if (requiredScopes.Count > 0)
        {
            parts.Add($"scope=\"{string.Join(" ", requiredScopes)}\"");
        }

        context.Response.Headers[HeaderNames.WWWAuthenticate] = string.Join(", ", parts);
    }

    private static string BuildWwwAuthenticateHeader(
        string metadataUrl,
        IReadOnlyList<string> scopes,
        string? errorCode,
        string? errorDescription)
    {
        var parts = new List<string> { $"Bearer resource_metadata=\"{metadataUrl}\"" };

        if (!string.IsNullOrEmpty(errorCode))
        {
            parts.Add($"error=\"{errorCode}\"");
        }

        if (!string.IsNullOrEmpty(errorDescription))
        {
            // Escape quotes in description
            var escapedDescription = errorDescription.Replace("\"", "\\\"");
            parts.Add($"error_description=\"{escapedDescription}\"");
        }

        if (scopes.Count > 0)
        {
            parts.Add($"scope=\"{string.Join(" ", scopes)}\"");
        }

        return string.Join(", ", parts);
    }
}
