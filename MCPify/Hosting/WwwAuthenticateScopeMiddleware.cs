using System.Linq;
using MCPify.Core.Auth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MCPify.Hosting;

/// <summary>
/// Ensures 401 challenges include a <c>scope</c> parameter on the WWW-Authenticate header
/// (RFC 6750 / MCP Authorization SHOULD), using scopes from <see cref="OAuthConfigurationStore"/>.
/// </summary>
internal sealed class WwwAuthenticateScopeMiddleware
{
    private readonly RequestDelegate _next;

    public WwwAuthenticateScopeMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            if (context.Response.StatusCode != StatusCodes.Status401Unauthorized &&
                context.Response.StatusCode != StatusCodes.Status403Forbidden)
            {
                return Task.CompletedTask;
            }

            var scopes = ResolveScopes(context);
            if (scopes.Count == 0)
            {
                return Task.CompletedTask;
            }

            var scopeValue = string.Join(' ', scopes);
            if (context.Response.Headers.TryGetValue("WWW-Authenticate", out var existing) && existing.Count > 0)
            {
                var updated = existing.Select(header => AppendScopeIfBearer(header, scopeValue)).ToArray();
                context.Response.Headers["WWW-Authenticate"] = updated;
            }
            else if (context.Response.StatusCode == StatusCodes.Status401Unauthorized)
            {
                // Challenge may have used another scheme; still advertise Bearer scope guidance.
                context.Response.Headers.Append("WWW-Authenticate", $"Bearer scope=\"{scopeValue}\"");
            }

            return Task.CompletedTask;
        });

        await _next(context);
    }

    private static IReadOnlyList<string> ResolveScopes(HttpContext context)
    {
        var store = context.RequestServices.GetService<OAuthConfigurationStore>();
        if (store is null)
        {
            return Array.Empty<string>();
        }

        var scopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var config in store.GetConfigurations())
        {
            foreach (var scope in config.Scopes.Keys)
            {
                if (!string.IsNullOrWhiteSpace(scope))
                {
                    scopes.Add(scope.Trim());
                }
            }
        }

        return scopes.OrderBy(s => s, StringComparer.Ordinal).ToList();
    }

    internal static string AppendScopeIfBearer(string? header, string scopeValue)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return $"Bearer scope=\"{scopeValue}\"";
        }

        if (!header.StartsWith("Bearer", StringComparison.OrdinalIgnoreCase))
        {
            return header;
        }

        if (header.Contains("scope=", StringComparison.OrdinalIgnoreCase))
        {
            return header;
        }

        return $"{header.TrimEnd()}, scope=\"{scopeValue}\"";
    }
}

/// <summary>
/// Inserts <see cref="WwwAuthenticateScopeMiddleware"/> early in the pipeline via <see cref="IStartupFilter"/>.
/// </summary>
internal sealed class WwwAuthenticateScopeStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.UseMiddleware<WwwAuthenticateScopeMiddleware>();
            next(app);
        };
    }
}

internal static class WwwAuthenticateScopeRegistration
{
    public static void AddWwwAuthenticateScopeMiddleware(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupFilter, WwwAuthenticateScopeStartupFilter>());
    }
}
