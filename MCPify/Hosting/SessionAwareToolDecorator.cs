using System.Linq;
using System.Text.Json;
using MCPify.Core;
using MCPify.Core.Auth;
using MCPify.Core.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Security.Claims;

namespace MCPify.Hosting;

/// <summary>
/// Decorates an McpServerTool to ensure a Session Context exists before execution.
/// This is crucial for Stdio transport where ASP.NET Core middleware does not run.
/// Also enforces per-tool <see cref="ScopeRequirement"/> metadata via <see cref="IAuthorizationService"/>.
/// </summary>
public class SessionAwareToolDecorator : McpServerTool
{
    private readonly McpServerTool _innerTool;
    private readonly IServiceProvider _serviceProvider;

    public SessionAwareToolDecorator(McpServerTool innerTool, IServiceProvider serviceProvider)
    {
        _innerTool = innerTool;
        _serviceProvider = serviceProvider;
    }

    public override Tool ProtocolTool => _innerTool.ProtocolTool;
    public override IReadOnlyList<object> Metadata => _innerTool.Metadata;

    public override async ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> context, CancellationToken token)
    {
        if (_innerTool.ProtocolTool.Name.Equals("connect", StringComparison.OrdinalIgnoreCase))
        {
            return await _innerTool.InvokeAsync(context, token);
        }

        var services = context.Services ?? _serviceProvider;
        var accessor = services.GetService<IMcpContextAccessor>();

        if (accessor == null)
        {
            return await _innerTool.InvokeAsync(context, token);
        }

        var previousSessionId = accessor.SessionId;
        var previousConnectionId = accessor.ConnectionId;
        var previousAccessToken = accessor.AccessToken;

        try
        {
            var sessionId = context.Server?.SessionId;
            var options = services.GetService<McpifyOptions>();
            var sessionMap = services.GetService<ISessionMap>();

            if (string.IsNullOrEmpty(sessionId) && context.Params?.Arguments != null)
            {
                var argEntry = context.Params.Arguments.FirstOrDefault(x => x.Key.Equals("sessionId", StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(argEntry.Key))
                {
                    if (argEntry.Value.ValueKind == JsonValueKind.String)
                    {
                        sessionId = argEntry.Value.GetString();
                    }
                    else
                    {
                        sessionId = argEntry.Value.ToString();
                    }
                }
            }

            if (string.IsNullOrEmpty(sessionId) &&
                options?.Transport == McpTransportType.Stdio &&
                sessionMap != null)
            {
                var bridgedSession = sessionMap.ResolvePrincipal(Constants.DefaultSessionId);
                if (!string.Equals(bridgedSession, Constants.DefaultSessionId, StringComparison.Ordinal))
                {
                    sessionId = bridgedSession;
                }
            }

            if (sessionMap != null && !string.IsNullOrEmpty(sessionId))
            {
                sessionId = sessionMap.ResolvePrincipal(sessionId);
            }

            accessor.SessionId = sessionId;

            var httpContextAccessor = services.GetService<IHttpContextAccessor>();
            var authHeader = httpContextAccessor?.HttpContext?.Request?.Headers["Authorization"].FirstOrDefault();
            accessor.AccessToken = string.IsNullOrEmpty(authHeader) ? null : authHeader;

            // Enforce per-tool ScopeRequirement metadata
            var scopeError = await TryEnforceScopesAsync(services, httpContextAccessor, accessor, token);
            if (scopeError != null)
            {
                return scopeError;
            }

            return await _innerTool.InvokeAsync(context, token);
        }
        finally
        {
            accessor.SessionId = previousSessionId;
            accessor.ConnectionId = previousConnectionId;
            accessor.AccessToken = previousAccessToken;
        }
    }

    /// <summary>
    /// Evaluates <see cref="ScopeRequirement"/> objects from the inner tool's <see cref="McpServerTool.Metadata"/>
    /// against the current principal. Returns an error result if authorization fails, or null to proceed.
    /// </summary>
    private async Task<CallToolResult?> TryEnforceScopesAsync(
        IServiceProvider services,
        IHttpContextAccessor? httpContextAccessor,
        IMcpContextAccessor mcpContextAccessor,
        CancellationToken token)
    {
        var scopeRequirements = _innerTool.Metadata.OfType<ScopeRequirement>().ToList();
        if (scopeRequirements.Count == 0)
        {
            return null;
        }

        var principal = ResolvePrincipal(services, httpContextAccessor, mcpContextAccessor);
        if (principal == null)
        {
            return ScopeError("Authentication required. No authenticated principal found.");
        }

        var authService = services.GetService<IAuthorizationService>();
        if (authService == null)
        {
            // Fail-closed: if the tool has scope requirements but no IAuthorizationService is
            // registered, deny the call rather than silently allowing it.
            return ScopeError("Scope enforcement is configured but IAuthorizationService is not registered. Call services.AddAuthorization() to enable scope checks.");
        }

        var result = await authService.AuthorizeAsync(principal, null, scopeRequirements);
        if (!result.Succeeded)
        {
            var failed = result.Failure?.FailedRequirements.OfType<ScopeRequirement>().FirstOrDefault();
            var pattern = failed?.Pattern ?? "*";
            return ScopeError($"Insufficient scope. Required scope pattern: '{pattern}'.");
        }

        return null;
    }

    /// <summary>
    /// Resolves the <see cref="ClaimsPrincipal"/> for scope evaluation.
    /// For HTTP: uses <see cref="HttpContext.User"/> (populated by auth middleware).
    /// For Stdio: parses the JWT access token from <see cref="IMcpContextAccessor.AccessToken"/>.
    /// </summary>
    private static ClaimsPrincipal? ResolvePrincipal(
        IServiceProvider services,
        IHttpContextAccessor? httpContextAccessor,
        IMcpContextAccessor mcpContextAccessor)
    {
        // HTTP path: the auth middleware has already populated HttpContext.User
        var httpContext = httpContextAccessor?.HttpContext;
        if (httpContext?.User?.Identity?.IsAuthenticated == true)
        {
            return httpContext.User;
        }

        // Stdio path: try to build a principal from the access token in the MCP context
        var accessToken = mcpContextAccessor.AccessToken;
        if (string.IsNullOrEmpty(accessToken))
        {
            return null;
        }

        // Strip "Bearer " prefix if present
        var token = accessToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? accessToken["Bearer ".Length..]
            : accessToken;

        return JwtClaimsPrincipalBuilder.BuildFromJwt(token);
    }

    private static CallToolResult ScopeError(string message) => new()
    {
        IsError = true,
        Content = new[] { new TextContentBlock { Text = $"Forbidden: {message}" } }
    };
}
