using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace MCPify.Core.Session;

/// <summary>
/// Resolves the application-level session handle used for token-store keying and context.
/// On HTTP, free-form client <c>sessionId</c> arguments are not trusted: they are ignored
/// unless they match a host-bound identity (transport session, <see cref="McpifyOptions.SessionIdResolver"/>,
/// <c>HttpContext.Items["McpSessionId"]</c>, or authenticated principal subject). A mismatch is rejected.
/// Stdio keeps DX-friendly client/default-session behavior for single-user desktop hosts.
/// </summary>
internal static class SessionHandleResolver
{
    internal const string HttpContextItemKey = "McpSessionId";

    internal readonly record struct Result(string? SessionId, string? Error);

    internal static Result Resolve(
        McpifyOptions? options,
        string? transportSessionId,
        IEnumerable<KeyValuePair<string, JsonElement>>? arguments,
        HttpContext? httpContext,
        ISessionMap? sessionMap)
    {
        var isStdio = options?.Transport == McpTransportType.Stdio;
        var clientArg = TryGetSessionIdArgument(arguments);
        var trusted = ResolveTrustedHandle(options, transportSessionId, httpContext);

        string? sessionId;

        if (isStdio)
        {
            sessionId = FirstNonEmpty(transportSessionId, clientArg, trusted);

            if (string.IsNullOrEmpty(sessionId) && sessionMap != null)
            {
                var bridged = sessionMap.ResolvePrincipal(Constants.DefaultSessionId);
                if (!string.Equals(bridged, Constants.DefaultSessionId, StringComparison.Ordinal))
                {
                    sessionId = bridged;
                }
            }
        }
        else
        {
            // HTTP (and any non-Stdio): fail closed on free-form client session handles.
            if (!string.IsNullOrEmpty(clientArg))
            {
                if (string.IsNullOrEmpty(trusted))
                {
                    // Do not adopt a client-chosen bucket when nothing host-bound exists.
                    sessionId = null;
                }
                else if (!string.Equals(clientArg, trusted, StringComparison.Ordinal))
                {
                    return new Result(
                        null,
                        "sessionId does not match the authenticated session handle. " +
                        "On HTTP, tool argument sessionId cannot select another user's token store. " +
                        "Bind tokens via SessionIdResolver or the authenticated principal (e.g. sub).");
                }
                else
                {
                    sessionId = trusted;
                }
            }
            else
            {
                sessionId = trusted;
            }
        }

        if (sessionMap != null && !string.IsNullOrEmpty(sessionId))
        {
            sessionId = sessionMap.ResolvePrincipal(sessionId);
        }

        return new Result(sessionId, null);
    }

    /// <summary>
    /// Host- or transport-bound handles only (never client tool arguments).
    /// Prefer host policy and authenticated identity over transport session ids so
    /// ServerManaged token buckets stay bound to the principal under stateless HTTP.
    /// </summary>
    internal static string? ResolveTrustedHandle(
        McpifyOptions? options,
        string? transportSessionId,
        HttpContext? httpContext)
    {
        if (httpContext != null)
        {
            if (options?.SessionIdResolver != null)
            {
                var resolved = options.SessionIdResolver(httpContext);
                if (!string.IsNullOrEmpty(resolved))
                {
                    return resolved;
                }
            }

            if (httpContext.Items.TryGetValue(HttpContextItemKey, out var item) &&
                item is string itemSession &&
                !string.IsNullOrEmpty(itemSession))
            {
                return itemSession;
            }

            var subject = TryGetPrincipalSubject(httpContext.User);
            if (!string.IsNullOrEmpty(subject))
            {
                return subject;
            }
        }

        // Stateful HTTP / legacy: server-issued transport session when no host-bound identity exists.
        return string.IsNullOrEmpty(transportSessionId) ? null : transportSessionId;
    }

    internal static string? TryGetSessionIdArgument(IEnumerable<KeyValuePair<string, JsonElement>>? arguments)
    {
        if (arguments == null)
        {
            return null;
        }

        foreach (var entry in arguments)
        {
            if (!entry.Key.Equals("sessionId", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (entry.Value.ValueKind == JsonValueKind.String)
            {
                var s = entry.Value.GetString();
                return string.IsNullOrEmpty(s) ? null : s;
            }

            var text = entry.Value.ToString();
            return string.IsNullOrEmpty(text) ? null : text;
        }

        return null;
    }

    internal static string? TryGetPrincipalSubject(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return user.FindFirst("sub")?.Value
            ?? user.FindFirst("oid")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst(ClaimTypes.Name)?.Value;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrEmpty(v))
            {
                return v;
            }
        }

        return null;
    }
}
