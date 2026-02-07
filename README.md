# MCPify

[![NuGet](https://img.shields.io/nuget/v/MCPify.svg)](https://www.nuget.org/packages/MCPify/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**MCPify** is a .NET library that bridges the gap between your existing ASP.NET Core APIs (or external OpenAPI/Swagger specs) and the [Model Context Protocol (MCP)](https://modelcontextprotocol.io/). It allows you to expose API operations as MCP tools that can be consumed by AI assistants like Claude Desktop, ensuring seamless integration with your existing services.

> **Latest Release:** v0.0.14-preview - Extensible UpstreamAuth abstraction + RFC 8693 Token Exchange!

## What's New

### v0.0.14-preview (Latest)
-   **UpstreamAuth Abstraction**: Replaces the rigid `TokenSource` enum with an extensible class offering static factory methods: `PassThrough()`, `None()`, `ServerManaged()`, `TokenExchange()`, `Fallback()`, and `Custom()`
-   **RFC 8693 Token Exchange**: New `UpstreamAuth.TokenExchange()` strategy exchanges the MCP client's access token for an upstream API token at a configured token endpoint
-   **Composable Strategies**: `UpstreamAuth.Fallback()` chains multiple strategies; `UpstreamAuth.Custom()` provides an escape hatch for user-supplied `ITokenProvider` factories
-   **Migration Path**: `TokenSource`/`AuthenticationFactory` remain available only as obsolete compatibility APIs; new configuration should use `UpstreamAuth`
-   **Modular Service Registration**: Split `AddMcpify()` into `AddMcpifyCore()` + `AddMcpifyAuthentication()` for granular control
-   **Lightweight Core Mode**: Use `AddMcpifyCore()` alone when using `UpstreamAuth.PassThrough()` or `UpstreamAuth.None()` to avoid unnecessary auth overhead
-   **Fixed Pass-Through Token Forwarding**: HTTP Authorization header extraction supports client token pass-through scenarios
-   **OAuth Discovery in Core**: `OpenApiOAuthParser` and `OAuthConfigurationStore` moved to core for metadata discovery without full auth stack
-   **Removed BuildServiceProvider Anti-Pattern**: `AddMcpifyAuthentication()` no longer builds an intermediate service provider
-   **Deployment Risk Documentation**: Clear guidance on HTTP pass-through safety and opt-in requirements
-   **Backward Compatible**: Existing `TokenSource`/`AuthenticationFactory` properties remain `[Obsolete]` for migration guidance

### v0.0.13-preview (Jan 27, 2026)
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
        UpstreamAuth = UpstreamAuth.ServerManaged(sp =>
            sp.GetRequiredService<OAuthAuthorizationCodeAuthentication>())
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

## Modular Service Registration

MCPify provides flexible service registration options to match your authentication needs. You can choose between the full-featured `AddMcpify()` or use the granular `AddMcpifyCore()` and `AddMcpifyAuthentication()` methods.

### Core Only (No Auth Overhead)

Use `AddMcpifyCore()` when your MCP client handles authentication or when no authentication is needed:

```csharp
// Lightweight setup for client-managed auth or public APIs
builder.Services.AddMcpifyCore(options =>
{
    options.Transport = McpTransportType.Stdio;

    // Client provides tokens - no server-side auth needed
    options.ExternalApis.Add(new ExternalApiOptions
    {
        ApiBaseUrl = "https://api.github.com",
        OpenApiUrl = "https://raw.githubusercontent.com/.../api.github.com.json",
        UpstreamAuth = UpstreamAuth.PassThrough()
    });

    // Or for public APIs with no auth
    options.ExternalApis.Add(new ExternalApiOptions
    {
        ApiBaseUrl = "https://api.publicapis.org",
        OpenApiUrl = "https://api.publicapis.org/swagger.json",
        UpstreamAuth = UpstreamAuth.None()
    });
});
```

This registers only essential services:
- `McpifyOptions`, `McpServerPrimitiveCollection`, `McpifyServiceRegistrar`
- `ISessionMap`, `IMcpContextAccessor`
- MCP Server transport (Stdio/Http)
- `HttpClient`, `IOpenApiProvider`, `IJsonSchemaGenerator`, `IEndpointMetadataProvider`

### Full Stack (Recommended)

Use `AddMcpify()` for the complete feature set including server-side OAuth:

```csharp
// Full setup with server-managed authentication
builder.Services.AddMcpify(options =>
{
    options.Transport = McpTransportType.Http;

    options.ExternalApis.Add(new ExternalApiOptions
    {
        ApiBaseUrl = "https://api.example.com",
        OpenApiUrl = "https://api.example.com/swagger.json",
        UpstreamAuth = UpstreamAuth.ServerManaged(sp =>
            sp.GetRequiredService<OAuthAuthorizationCodeAuthentication>())
    });
});
```

This is equivalent to calling both methods:
```csharp
builder.Services.AddMcpifyCore(options => { ... });
builder.Services.AddMcpifyAuthentication();
```

### Core + Selective Authentication

For advanced scenarios, register core first then add authentication:

```csharp
builder.Services.AddMcpifyCore(options =>
{
    options.Transport = McpTransportType.Http;

    // Mix of auth strategies
    options.ExternalApis.Add(new ExternalApiOptions
    {
        ApiBaseUrl = "https://api.example.com",
        OpenApiUrl = "https://api.example.com/swagger.json",
        UpstreamAuth = UpstreamAuth.Fallback(
            UpstreamAuth.PassThrough(),
            UpstreamAuth.ServerManaged(sp =>
                sp.GetRequiredService<OAuthAuthorizationCodeAuthentication>()))
    });
});

// Add auth services (LoginTool, SessionManagementTool, ISecureTokenStore, etc.)
builder.Services.AddMcpifyAuthentication();
```

### Service Registration Breakdown

| Service | AddMcpifyCore | AddMcpifyAuthentication |
|---------|:-------------:|:-----------------------:|
| `McpifyOptions` | ✓ | |
| `McpServerPrimitiveCollection` | ✓ | |
| `McpifyServiceRegistrar` | ✓ | |
| `ISessionMap` | ✓ | |
| `IMcpContextAccessor` | ✓ | |
| `IHttpContextAccessor` | ✓ | |
| `IOpenApiProvider` | ✓ | |
| `IJsonSchemaGenerator` | ✓ | |
| `IEndpointMetadataProvider` | ✓ | |
| `OpenApiOAuthParser` | ✓ | |
| `OAuthConfigurationStore` | ✓ | |
| `ISecureTokenStore` | | ✓ |
| `LoginTool` | | ✓ |
| `SessionManagementTool` | | ✓ |
| `ScopeRequirementHandler` | | ✓ |
| `McpAuthenticationHandler` | | ✓ |

## Authentication

MCPify provides comprehensive OAuth 2.0 authentication support with automatic token management, validation, and scope enforcement. You can choose whether MCPify manages authentication (server-side) or your MCP client provides tokens directly (client-side).

### UpstreamAuth (Recommended)

`UpstreamAuth` is the recommended way to configure how MCPify acquires tokens for upstream API calls. It replaces the older `TokenSource` enum with an extensible, composable abstraction.

```csharp
builder.Services.AddMcpify(options =>
{
    // Public API — no auth
    options.ExternalApis.Add(new ExternalApiOptions
    {
        ApiBaseUrl = "https://api.publicapis.org",
        OpenApiUrl = "https://api.publicapis.org/swagger.json",
        UpstreamAuth = UpstreamAuth.None()
    });

    // Client provides tokens directly
    options.ExternalApis.Add(new ExternalApiOptions
    {
        ApiBaseUrl = "https://api.github.com",
        OpenApiUrl = "https://raw.githubusercontent.com/.../api.github.com.json",
        UpstreamAuth = UpstreamAuth.PassThrough()
    });

    // Server manages OAuth flows
    options.ExternalApis.Add(new ExternalApiOptions
    {
        ApiBaseUrl = "https://api.example.com",
        OpenApiUrl = "https://api.example.com/swagger.json",
        UpstreamAuth = UpstreamAuth.ServerManaged(sp =>
            sp.GetRequiredService<OAuthAuthorizationCodeAuthentication>())
    });

    // Hybrid — try client token first, then server auth
    options.ExternalApis.Add(new ExternalApiOptions
    {
        ApiBaseUrl = "https://api.example.com",
        OpenApiUrl = "https://api.example.com/swagger.json",
        UpstreamAuth = UpstreamAuth.Fallback(
            UpstreamAuth.PassThrough(),
            UpstreamAuth.ServerManaged(sp =>
                sp.GetRequiredService<OAuthAuthorizationCodeAuthentication>()))
    });
});
```

`UpstreamAuth.PassThrough()` (or `UpstreamAuth.Fallback()` with `PassThrough()` first) on HTTP transport requires explicit opt-in:

```csharp
builder.Services.AddMcpify(options =>
{
    options.Transport = McpTransportType.Http;
    options.AllowClientTokenPassthrough = true;
});
```

Without `AllowClientTokenPassthrough = true`, MCPify fails fast at startup for HTTP.

#### Token Exchange (RFC 8693)

Exchange the MCP client's access token for an upstream API token at an authorization server. Requires `AddMcpifyAuthentication()` for the secure token store:

```csharp
builder.Services.AddMcpifyCore(options =>
{
    options.ExternalApis.Add(new ExternalApiOptions
    {
        ApiBaseUrl = "https://api.internal.com",
        OpenApiUrl = "https://api.internal.com/swagger.json",
        UpstreamAuth = UpstreamAuth.TokenExchange(opts =>
        {
            opts.TokenEndpoint = "https://auth.example.com/token";
            opts.ClientId = "mcpify-client";
            opts.ClientSecret = "secret";
            opts.Scope = "api.read api.write";
            opts.Audience = "internal-api";       // RFC 8693
            opts.Resource = "https://api.internal.com";  // RFC 8707
        })
    });
});
builder.Services.AddMcpifyAuthentication(); // Required for ISecureTokenStore
```

#### Custom Token Provider

Escape hatch for advanced scenarios:

```csharp
options.ExternalApis.Add(new ExternalApiOptions
{
    ApiBaseUrl = "https://api.example.com",
    OpenApiUrl = "https://api.example.com/swagger.json",
    UpstreamAuth = UpstreamAuth.Custom(sp =>
        sp.GetRequiredService<MyCustomTokenProvider>())
});
```

#### Migration from TokenSource

| Old API | New API |
|---------|---------|
| `TokenSource = TokenSource.None` | `UpstreamAuth = UpstreamAuth.None()` |
| `TokenSource = TokenSource.Client` | `UpstreamAuth = UpstreamAuth.PassThrough()` |
| `TokenSource = TokenSource.Server` + `AuthenticationFactory = ...` | `UpstreamAuth = UpstreamAuth.ServerManaged(...)` |
| `TokenSource = TokenSource.Both` + `AuthenticationFactory = ...` | `UpstreamAuth = UpstreamAuth.Fallback(UpstreamAuth.PassThrough(), UpstreamAuth.ServerManaged(...))` |

> **Note:** `TokenSource` and `AuthenticationFactory` are `[Obsolete]` and kept only for migration. Use `UpstreamAuth` for all new configuration.

### Deployment-Specific Auth Considerations

| Deployment | Transport | `UpstreamAuth.PassThrough()` | `UpstreamAuth.ServerManaged(...)` | `UpstreamAuth.Fallback(PassThrough, ServerManaged)` |
|------------|-----------|-------------------------------|------------------------------------|-----------------------------------------------------|
| Local / single-user | Stdio | Safe | Safe | Safe |
| Hosted / multi-user | Http | Explicit opt-in only | Recommended default | Explicit opt-in only |

For HTTP transport, pass-through (or fallback with pass-through first) requires `AllowClientTokenPassthrough = true`. Without that flag, MCPify fails fast at startup.

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

### Manual Client Registration vs DCR

For server-managed upstream OAuth in MCPify, dynamic client registration is not required.
You can register your OAuth client manually with your authorization server and configure:

- `client_id`
- `client_secret`
- authorization endpoint
- token endpoint
- redirect URI handled by your app callback

Enable DCR only if your MCP client requires it to connect (for example, ChatGPT over HTTP).

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
