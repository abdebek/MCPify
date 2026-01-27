# MCPify

[![NuGet](https://img.shields.io/nuget/v/MCPify.svg)](https://www.nuget.org/packages/MCPify/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**MCPify** is a .NET library that bridges the gap between your existing ASP.NET Core APIs (or external OpenAPI/Swagger specs) and the [Model Context Protocol (MCP)](https://modelcontextprotocol.io/). It allows you to expose API operations as MCP tools that can be consumed by AI assistants like Claude Desktop, ensuring seamless integration with your existing services.

> **Latest Release:** v0.0.13-preview - Now with flexible token provider architecture and client-managed authentication!

## What's New

### v0.0.13-preview (Latest - Jan 27, 2026)
-   **Flexible Token Provider Architecture**: New `ITokenProvider` abstraction separates token acquisition from token attachment
-   **Client-Managed Authentication**: Native support for MCP clients providing tokens via `TokenSource.Client`
-   **TokenSource Configuration**: Explicit control over token sourcing with `Server`, `Client`, `Both`, or `None` options
-   **No More Placeholder Auth**: Eliminates the need for placeholder authentication configurations when clients handle OAuth
-   **Factory Pattern**: Consolidated token provider creation via `TokenProviderFactory`
-   **Better Testability**: Simplified mocking and testing with clean `ITokenProvider` interface

### v0.0.12 (Jan 23, 2026)
-   **Enhanced OAuth Middleware**: Improved OAuth authentication middleware with better error handling and token management (#17)
-   **JWT Token Validation**: Full support for JWT access token validation including expiration, audience, and scope verification (#15)
-   **Per-Tool Scope Requirements**: Define granular scope requirements for specific tools using pattern matching (#15)
-   **Automatic Scope Discovery**: Scopes are automatically extracted from OpenAPI security schemes and enforced during validation (#15)
-   **WWW-Authenticate Header**: Improved WWW-Authenticate header to include scope parameter per MCP spec
-   **LoginBrowserBehavior**: Control browser launch behavior for OAuth login in headless environments
-   **OAuth2Configuration List**: Support for multiple OAuth providers with AuthorizationServers exposure (#13)

## Features

-   **Automatic Tool Generation**: Dynamically converts OpenAPI (Swagger) v2/v3 definitions into MCP tools.
-   **Hybrid Support**: Expose your **local** ASP.NET Core endpoints and **external** public APIs simultaneously.
-   **Seamless Authentication**: Built-in support for OAuth 2.0 Authorization Code Flow with PKCE.
    -   Includes a `login_auth_code_pkce` tool that handles the browser-based login flow automatically.
    -   Securely stores tokens per session using encrypted local storage.
    -   Automatically refreshes tokens when they expire.
-   **MCP Authorization Spec Compliant**: Full compliance with the [MCP Authorization Specification](https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization).
    -   Protected Resource Metadata (`/.well-known/oauth-protected-resource`)
    -   RFC 8707 Resource Parameter support
    -   JWT token validation (expiration, audience, scopes)
    -   403 Forbidden with `insufficient_scope` error
-   **Dual Transport**: Supports both `Stdio` (for local desktop apps like Claude) and `Http` (SSE) transports.
-   **Production Ready**: Robust logging, error handling, and configurable options.

## Supported Frameworks

-   .NET 8, .NET 9, .NET 10

## Quick Start

### 1. Installation

Install the package into your ASP.NET Core project:

```bash
dotnet add package MCPify
```

### 2. Configuration

Add MCPify to your `Program.cs`:

```csharp
using MCPify.Hosting;

var builder = WebApplication.CreateBuilder(args);

// ... Add other services ...

// Add MCPify services
builder.Services.AddMcpify(options =>
{
    // Stdio for local tools (Claude Desktop), Http for remote servers
    options.Transport = McpTransportType.Stdio;

    // Option A: Expose Local Endpoints
    options.LocalEndpoints = new LocalEndpointsOptions
    {
        Enabled = true,
        ToolPrefix = "myapp_",
        BaseUrlOverride = "https://localhost:5001",  // Optional: override base URL
        Filter = op => op.Route.StartsWith("/api"),  // Optional: filter endpoints
        TokenSource = TokenSource.Both,  // Optional: defaults to Both (hybrid)
        AuthenticationFactory = sp => sp.GetRequiredService<OAuthAuthorizationCodeAuthentication>()  // Optional
    };

    // Option B: Expose External APIs from URL
    options.ExternalApis.Add(new ExternalApiOptions
    {
        ApiBaseUrl = "https://petstore.swagger.io/v2",
        OpenApiUrl = "https://petstore.swagger.io/v2/swagger.json",
        ToolPrefix = "petstore_"
    });

    // Option C: Expose External APIs from Local File
    options.ExternalApis.Add(new ExternalApiOptions
    {
        ApiBaseUrl = "https://api.example.com",
        OpenApiFilePath = "path/to/openapi-spec.json",  // or .yaml
        ToolPrefix = "myapi_"
    });
});

var app = builder.Build();

// Add Middleware (order matters!)
app.UseAuthentication();
app.UseAuthorization();

// ... Map your endpoints ...

// Map the MCP endpoint (required for Http transport)
app.MapMcpifyEndpoint();

app.Run();
```

### 3. Usage with Claude Desktop

To use your MCPify app with [Claude Desktop](https://claude.ai/download), edit your config file (`%APPDATA%\Claude\claude_desktop_config.json` on Windows or `~/Library/Application Support/Claude/claude_desktop_config.json` on macOS):

```json
{
  "mcpServers": {
    "my-app": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/absolute/path/to/YourProject.csproj", 
        "--",
        "--Mcpify:Transport=Stdio"
      ]
    }
  }
}
```

> **Note:** When using `dotnet run`, ensure your application does not print build logs to stdout, as this corrupts the MCP JSON-RPC protocol. You can suppress logs or publish your app as a single-file executable for a cleaner setup.

## Authentication

MCPify provides comprehensive OAuth 2.0 authentication support with automatic token management, validation, and scope enforcement. You can choose whether MCPify manages authentication (server-side) or your MCP client provides tokens directly (client-side).

### Token Source Configuration

MCPify supports flexible token sourcing through the `TokenSource` configuration option. This allows you to control where authentication tokens come from:

#### Hybrid Approach (Default - Recommended)

Try client token first, fallback to server authentication - provides maximum flexibility:

```csharp
builder.Services.AddMcpify(options =>
{
    options.ExternalApis.Add(new ExternalApiOptions
    {
        ApiBaseUrl = "https://api.example.com",
        OpenApiUrl = "https://api.example.com/swagger.json",
        TokenSource = TokenSource.Both,  // Default - best of both worlds
        AuthenticationFactory = sp => new OAuthAuthorizationCodeAuthentication(...)
    });
});
```

This is the recommended default because:
- If the MCP client provides a token → uses it automatically
- If no client token → falls back to server authentication
- Backward compatible with existing configurations

#### Server-Managed Authentication

MCPify exclusively handles OAuth flows, token storage, and refresh:

```csharp
builder.Services.AddMcpify(options =>
{
    options.ExternalApis.Add(new ExternalApiOptions
    {
        ApiBaseUrl = "https://api.example.com",
        OpenApiUrl = "https://api.example.com/swagger.json",
        TokenSource = TokenSource.Server,  // Default - MCPify manages auth
        AuthenticationFactory = sp => new OAuthAuthorizationCodeAuthentication(...)
    });
});
```

#### Client-Managed Authentication

Your MCP client (e.g., Claude Desktop) handles authentication and provides tokens:

```csharp
builder.Services.AddMcpify(options =>
{
    options.ExternalApis.Add(new ExternalApiOptions
    {
        ApiBaseUrl = "https://api.github.com",
        OpenApiUrl = "https://raw.githubusercontent.com/.../api.github.com.json",
        TokenSource = TokenSource.Client  // Client provides tokens
    });
});
```

This is useful when:
- Your MCP client handles OAuth flows
- You want to avoid duplicate authentication logic
- Tokens are managed outside MCPify

#### Hybrid Approach

Try client token first, fallback to server authentication:

```csharp
builder.Services.AddMcpify(options =>
{
    options.ExternalApis.Add(new ExternalApiOptions
    {
        ApiBaseUrl = "https://api.example.com",
        OpenApiUrl = "https://api.example.com/swagger.json",
        TokenSource = TokenSource.Both,  // Try client first, fallback to server
        AuthenticationFactory = sp => new BearerAuthentication("fallback-token")
    });
});
```

#### No Authentication

For public APIs that don't require authentication:

```csharp
builder.Services.AddMcpify(options =>
{
    options.ExternalApis.Add(new ExternalApiOptions
    {
        ApiBaseUrl = "https://api.publicapis.org",
        OpenApiUrl = "https://api.publicapis.org/swagger.json",
        TokenSource = TokenSource.None  // No auth required
    });
});
```

**Available TokenSource Values:**

| Value | Description |
|-------|-------------|
| `Both` (default) | Try client token first, fallback to server authentication - provides maximum flexibility |
| `Server` | MCPify manages authentication (OAuth flows, API keys, etc.) - explicit server-only auth |
| `Client` | MCP client provides tokens directly via the protocol - explicit client-only auth |
| `None` | No authentication required |

### Enabling OAuth

Register the authentication provider in your `Program.cs` (ensure this is done before calling `AddMcpify`):

```csharp
services.AddScoped<OAuthAuthorizationCodeAuthentication>(sp => {
    return new OAuthAuthorizationCodeAuthentication(
        clientId: "your-client-id",
        authorizationEndpoint: "https://auth.example.com/authorize",
        tokenEndpoint: "https://auth.example.com/token",
        scope: "api_access",
        secureTokenStore: sp.GetRequiredService<ISecureTokenStore>(),
        mcpContextAccessor: sp.GetRequiredService<IMcpContextAccessor>(),
        redirectUri: "http://localhost:5000/auth/callback" // Your app must handle this
    );
});

// Register the Login Tool
services.AddLoginTool(sp => new LoginTool());
```

### The Login Flow

1.  The user asks Claude: *"Please login"* or uses a tool that requires auth.
2.  Claude calls the `login_auth_code_pkce` tool.
3.  MCPify automatically opens the system browser to the login page (in interactive environments).
4.  The user logs in and approves the request.
5.  The browser redirects back to your application (e.g., `/auth/callback`).
6.  Your app saves the token and displays a success message.
7.  The `login_auth_code_pkce` tool detects the successful login and reports back to Claude.
8.  Claude can now invoke authenticated tools!

### Headless / Remote Environments

When running MCPify on headless servers, containers, or remote environments where a browser cannot be opened, you can configure the login behavior to skip browser launch attempts and immediately return the authorization URL:

```csharp
builder.Services.AddMcpify(options =>
{
    // For headless/remote environments - return URL immediately without browser launch
    options.LoginBrowserBehavior = BrowserLaunchBehavior.Never;
});
```

Available options for `LoginBrowserBehavior`:

| Value | Description |
|-------|-------------|
| `Auto` (default) | Automatically detects headless environments (no DISPLAY on Linux, SSH sessions, containers) and skips browser launch when appropriate. |
| `Always` | Always attempt to open the browser, regardless of environment. |
| `Never` | Never attempt to open the browser. Returns the authorization URL immediately for manual authentication. Ideal for headless servers and containers. |

With `Auto` mode, MCPify detects headless environments by checking:
-   **Linux**: Missing `DISPLAY` or `WAYLAND_DISPLAY` environment variables, SSH sessions without X forwarding, Docker containers
-   **Windows**: Container environments (Kubernetes, Docker)
-   **macOS**: SSH sessions

### Protected Resource Metadata & Challenges

MCPify now relies on the official `ModelContextProtocol.AspNetCore` authentication handler for OAuth 2.0. When you call `AddMcpify`, the MCP authentication scheme is registered automatically and the handler issues `WWW-Authenticate` challenges that point back to the protected resource metadata endpoint.

```csharp
builder.Services.AddMcpify(options =>
{
    // Set the resource URL for audience validation
    options.ResourceUrlOverride = "https://api.example.com";

    // Configure OAuth provider(s)
    options.OAuthConfigurations.Add(new OAuth2Configuration
    {
        AuthorizationUrl = "https://auth.example.com/authorize",
        TokenUrl = "https://auth.example.com/token",
        Scopes = new Dictionary<string, string>
        {
            { "read", "Read access" },
            { "write", "Write access" }
        }
    });

    // Enable token validation (opt-in for backward compatibility)
    options.TokenValidation = new TokenValidationOptions
    {
        EnableJwtValidation = true,
        ValidateAudience = true,
        ValidateScopes = true,
        RequireOAuthConfiguredScopes = true,  // Auto-require scopes from OAuth config
        ClockSkew = TimeSpan.FromMinutes(5)
    };
});
```

If you need to customize the advertised metadata—for example to add documentation links or override the detected resource URL—you can configure `McpAuthenticationOptions`:

```csharp
builder.Services.PostConfigure<McpAuthenticationOptions>(options =>
{
    options.ResourceMetadata ??= new ProtectedResourceMetadata();
    options.ResourceMetadata.Documentation = new Uri("https://docs.example.com/mcp");
});
```

Ensure your middleware pipeline includes `app.UseAuthentication();` and `app.UseAuthorization();` so that the handler can participate in requests. Challenges no longer run through a custom middleware; the standard ASP.NET Core authentication flow handles everything.

### RFC 8707 Resource Parameter

MCPify automatically includes the [RFC 8707](https://datatracker.ietf.org/doc/html/rfc8707) `resource` parameter in OAuth requests when `ResourceUrlOverride` is configured. This helps authorization servers issue tokens scoped to specific resources:

```csharp
builder.Services.AddMcpify(options =>
{
    options.ResourceUrlOverride = "https://api.example.com";
});
```

The resource parameter is added to:
-   Authorization URL (`/authorize?resource=...`)
-   Token exchange requests (`POST /token` with `resource=...`)
-   Token refresh requests

## Contributing

We welcome contributions! Please see our [Contributing Guide](CONTRIBUTING.md) for details.

## License

MIT
