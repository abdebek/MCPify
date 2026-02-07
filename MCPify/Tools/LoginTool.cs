using MCPify.Core;
using MCPify.Core.Auth;
using MCPify.Core.Auth.OAuth;
using MCPify.Core.Session;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace MCPify.Tools;

public class LoginTool : McpServerTool
{
    public LoginTool()
    {
    }

    public override Tool ProtocolTool => new()
    {
        Name = "login_auth_code_pkce",
        Description = "Initiates the OAuth login flow. In interactive environments, attempts to open the system browser with the authorization URL and waits for the user to complete the login. In headless environments (detected automatically or configured via LoginBrowserBehavior option), returns the authorization URL immediately for manual authentication.",
        InputSchema = JsonDocument.Parse("""{ "type": "object", "properties": { "sessionId": { "type": "string" } } }""").RootElement
    };

    public override IReadOnlyList<object> Metadata => Array.Empty<object>();

    public override async ValueTask<CallToolResult> InvokeAsync(RequestContext<CallToolRequestParams> context, CancellationToken token)
    {
        var logger = context.Services != null ? context.Services.GetService<Microsoft.Extensions.Logging.ILogger<LoginTool>>() : null;
        var accessor = context.Services?.GetService<IMcpContextAccessor>();

        string? sessionId = context.Server?.SessionId;

        // 1. Try to get from arguments for backwards compatibility
        if (string.IsNullOrEmpty(sessionId) &&
            context.Params?.Arguments != null &&
            context.Params.Arguments.TryGetValue("sessionId", out var sessionElement) &&
            sessionElement.ValueKind == JsonValueKind.String &&
            !string.IsNullOrEmpty(sessionElement.GetString()))
        {
            sessionId = sessionElement.GetString();
        }

        // 2. Try to get from Context Accessor
        if (string.IsNullOrEmpty(sessionId) && accessor != null)
        {
            sessionId = accessor.SessionId;
        }

        // 3. Backend fallback: use a stable default session if none exists
        if (string.IsNullOrEmpty(sessionId))
        {
            sessionId = Constants.DefaultSessionId;
            if (accessor != null)
            {
                accessor.SessionId = sessionId;
            }
        }

        logger?.LogInformation("Initiating login for session {SessionId}", sessionId);

        var auth = context.Services!.GetRequiredService<OAuthAuthorizationCodeAuthentication>();
        var tokenStore = context.Services!.GetRequiredService<ISecureTokenStore>();
        var sessionMap = context.Services!.GetService<ISessionMap>();
        var options = context.Services!.GetService<McpifyOptions>() ?? new McpifyOptions();

        if (sessionMap != null)
        {
            sessionMap.UpgradeSession(Constants.DefaultSessionId, sessionId);
        }

        var authUrl = auth.BuildAuthorizationUrl(sessionId);
        var browserOpened = false;

        // Determine whether to attempt browser launch based on configuration
        if (ShouldOpenBrowser(options.LoginBrowserBehavior, logger))
        {
            try
            {
                logger?.LogDebug("Attempting to open browser for URL: {AuthUrl}", authUrl);
                OpenBrowser(authUrl);
                browserOpened = true;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Failed to open browser automatically.");
            }
        }
        else
        {
            logger?.LogInformation("Browser launch skipped (headless environment detected or disabled by configuration)");
            // Return the URL immediately without waiting - no browser means no need to poll
            return new CallToolResult
            {
                Content = new[] { new TextContentBlock { Text = $"Please open this URL to authenticate: {authUrl}" } }
            };
        }

        var timeout = DateTime.UtcNow.AddMinutes(2);
        while (DateTime.UtcNow < timeout && !token.IsCancellationRequested)
        {
            var tokenData = await tokenStore.GetTokenAsync(sessionId, "OAuth", token);
            if (tokenData != null && !string.IsNullOrEmpty(tokenData.AccessToken))
            {
                 if (options.Transport == McpTransportType.Stdio &&
                     !string.Equals(sessionId, Constants.DefaultSessionId, StringComparison.Ordinal))
                 {
                     await tokenStore.SaveTokenAsync(Constants.DefaultSessionId, "OAuth", tokenData, token);
                 }

                 logger?.LogInformation("Login successful for session {SessionId}", sessionId);
                 return new CallToolResult
                {
                    Content = new[] { new TextContentBlock { Text = $"Login successful! Session '{sessionId}' is now authenticated." } }
                };
            }

            try
            {
                await Task.Delay(1000, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        logger?.LogInformation("Login timed out or was cancelled for session {SessionId}", sessionId);

        var response = browserOpened
            ? $"Browser opened. Please complete the login there. If it didn't open, visit: {authUrl}"
            : $"Please open this URL to authenticate: {authUrl}";

        return new CallToolResult
        {
            Content = new[] { new TextContentBlock { Text = response } }
        };
    }

    private void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                url = url.Replace("&", "^&");
                Process.Start(new ProcessStartInfo("cmd", $"/c start {url}") { CreateNoWindow = true });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
        }
    }

    /// <summary>
    /// Determines whether the current environment supports browser launching.
    /// Returns false for headless environments like containers, SSH sessions without X forwarding, etc.
    /// </summary>
    private static bool IsHeadlessEnvironment()
    {
        // Windows environments generally support browser launching
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Check for common container indicators on Windows
            var isContainer = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CONTAINER"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST"));
            return isContainer;
        }

        // On Linux, check for display server availability
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Check for X11 or Wayland display
            var hasDisplay = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

            if (!hasDisplay)
            {
                return true; // No display = headless
            }

            // Check for SSH session without proper X forwarding
            var isSsh = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SSH_CONNECTION"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SSH_CLIENT"));

            if (isSsh && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            {
                return true; // SSH without X forwarding
            }

            // Check for common container indicators
            if (File.Exists("/.dockerenv") || File.Exists("/run/.containerenv"))
            {
                return true;
            }

            return false;
        }

        // On macOS, check for SSH session or container
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var isSsh = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SSH_CONNECTION"))
                || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SSH_CLIENT"));

            // SSH sessions on macOS can't open browser on the remote machine
            return isSsh;
        }

        // Unknown platform - assume headless for safety
        return true;
    }

    /// <summary>
    /// Determines whether the browser should be opened based on the configured behavior.
    /// </summary>
    private static bool ShouldOpenBrowser(BrowserLaunchBehavior behavior, Microsoft.Extensions.Logging.ILogger? logger)
    {
        switch (behavior)
        {
            case BrowserLaunchBehavior.Always:
                logger?.LogDebug("Browser launch behavior is set to Always - will attempt to open browser");
                return true;

            case BrowserLaunchBehavior.Never:
                logger?.LogDebug("Browser launch behavior is set to Never - skipping browser launch");
                return false;

            case BrowserLaunchBehavior.Auto:
            default:
                var isHeadless = IsHeadlessEnvironment();
                logger?.LogDebug("Browser launch behavior is Auto - headless detection: {IsHeadless}", isHeadless);
                return !isHeadless;
        }
    }

    private static CallToolResult Error(string message) => new()
    {
        IsError = true,
        Content = new[] { new TextContentBlock { Text = message } }
    };
}
