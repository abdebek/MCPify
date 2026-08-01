# MCPify

[![NuGet](https://img.shields.io/nuget/v/MCPify.svg)](https://www.nuget.org/packages/MCPify/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**MCPify** is a .NET library that turns OpenAPI/Swagger specs and ASP.NET Core endpoints into [Model Context Protocol (MCP)](https://modelcontextprotocol.io/) tools for AI assistants like Claude Desktop.

Two modes:

- **External API import** — pass an OpenAPI URL or file path + base URL. No ASP.NET Core host required; works in any .NET app via `AddMcpifyCore()`.
- **Local endpoint proxy + MCP server** — expose your own ASP.NET Core routes as tools and host the MCP endpoint. Requires an ASP.NET Core host via `AddMcpify()`.

> **Latest Release:** v0.0.15-preview - SDK 2.0, honesty/auth composition baseline, Playwright smokes (still preview)

## What's New

### v0.0.15-preview (Latest)
-   **MCP SDK 2.0.0** with Streamable HTTP labeling and `AddAuthorizationFilters`
-   **Auth honesty**: host JWT ownership, required `stateSecret`, PKCE default on, no unvalidated `sub` rekey, no default-scheme hijack
-   **Single registration path**: `MapMcpifyEndpoint` registers local + external tools
-   **SSRF guard**, scope fail-closed enforcement, TokenExchange error handling
-   **Playwright** Sample smoke tests (OAuth + MCP HTTP)
-   **Tool curation**: global + per-API cardinality caps, declarative `ToolFilter` (allow/deny by path, method, tag, operationId), improved schema quality (`additionalProperties`, nullable, defaults, `oneOf`/`anyOf`/`allOf`)
-   Stay on this preview line while building tool curation and further roadmap items (next free NuGet id after `0.0.14-preview`)

### v0.0.14-preview
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
-   **Per-Tool Scope Requirements**: Define granular scope requirements for specific tools using pattern matching (#15)
-   **Automatic Scope Discovery**: Scopes are automatically extracted from OpenAPI security schemes (#15)
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
-   **MCP Authorization Spec Compliant**: Partial compliance with the [MCP Authorization Specification](https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization).
    -   Protected Resource Metadata (`/.well-known/oauth-protected-resource`)
    -   RFC 8707 Resource Parameter support
    -   Per-tool scope requirements via `ScopeRequirement` metadata
    -   > **Note:** Inbound JWT token validation (expiration, audience, scope enforcement) is the host application's responsibility via standard ASP.NET Core `AddJwtBearer`. MCPify does not ship its own JWT validator. Scope enforcement via `ScopeRequirementHandler` requires `AddAuthorization` policies/filters to be wired by the host.
-   **Dual Transport**: Supports both `Stdio` (for local desktop apps like Claude) and `Http` (Streamable HTTP) transports.
-   **Production Ready**: Robust logging, error handling, and configurable options.

## Supported Frameworks

-   .NET 8, .NET 9, .NET 10

## Quick Start

### 1. Installation

Install the package into your project:

```bash
dotnet add package MCPify --version 0.0.15-preview
```

> **Preview notice:** MCPify is on the `0.0.x-preview` line. Breaking changes may occur between preview releases. Pin an exact version (e.g. `0.0.15-preview`) rather than a floating range to avoid surprises. A `0.1.0-preview` soft-freeze is planned once tool curation and local-tool DX are feature-complete.

### 2a. External APIs Only (No ASP.NET Core Host)

If you just want to expose external OpenAPI specs as MCP tools (no local endpoints, no MCP HTTP server), use `AddMcpifyCore()` with Stdio transport in any .NET console app:

```csharp
using MCPify.Hosting;

var builder = Host.CreateDefaultBuilder(args);
builder.Services.AddMcpifyCore(options =>
{
    options.Transport = McpTransportType.Stdio;
    options.ExternalApis.Add(new ExternalApiOptions
    {
        ApiBaseUrl = "https://petstore.swagger.io/v2",
        OpenApiUrl = "https://petstore.swagger.io/v2/swagger.json",
        ToolPrefix = "petstore_",
        UpstreamAuth = UpstreamAuth.None()
    });
});
var app = builder.Build();
app.MapMcpifyEndpoint();  // or use the Stdio transport directly
app.Run();
```

This works in a plain `Microsoft.NET.Sdk` console app — no `FrameworkReference` to ASP.NET Core needed for external-only import.

### 2b. Full Stack: Local Endpoints + MCP Server (ASP.NET Core Host)

To expose your own ASP.NET Core endpoints as tools and host the MCP endpoint:

```csharp
using MCPify.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMcpify(options =>
{
    options.Transport = McpTransportType.Stdio;

    // Expose local ASP.NET Core endpoints as tools
    options.LocalEndpoints = new LocalEndpointsOptions
    {
        Enabled = true,
        ToolPrefix = "myapp_",
        BaseUrlOverride = "https://localhost:5001",
        Filter = op => op.Route.StartsWith("/api"),
        UpstreamAuth = UpstreamAuth.ServerManaged(sp =>
            sp.GetRequiredService<OAuthAuthorizationCodeAuthentication>())
    };

    // Also import external APIs
    options.ExternalApis.Add(new ExternalApiOptions
    {
        ApiBaseUrl = "https://petstore.swagger.io/v2",
        OpenApiUrl = "https://petstore.swagger.io/v2/swagger.json",
        ToolPrefix = "petstore_"
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

#### Friendly OAuth registration (OpenAPI-aware)

Use `AddOAuthAuthorizationCodeAuthenticator` to build server-managed auth from options or an OpenAPI OAuth2 **authorization_code** security scheme (issue #2):

```csharp
builder.Services.AddMcpifyAuthentication();
builder.Services.AddOAuthAuthorizationCodeAuthenticator(options =>
{
    options.ClientId = "my-client";
    options.ClientSecret = "secret"; // optional for public clients
    options.RedirectUri = "https://localhost:5001/auth/callback";
    options.StateSecret = builder.Configuration["Demo:StateSecret"]; // ≥ 32 chars
    // Either set endpoints explicitly...
    // options.AuthorizationEndpoint = "https://auth.example.com/authorize";
    // options.TokenEndpoint = "https://auth.example.com/token";
    // ...or load them from OpenAPI:
    options.OpenApiUrl = "https://api.example.com/swagger.json";
});
```

#### Login strategy (remote / headless)

```csharp
builder.Services.AddMcpify(options =>
{
    // Authorization-code flow: Auto | Always | Never open a browser (#9)
    options.LoginBrowserBehavior = BrowserLaunchBehavior.Never;

    // Or use device code (register DeviceCodeAuthentication in DI first)
    // options.LoginFlow = LoginFlow.DeviceCode;
});
```

Outbound auth on tools uses **`UpstreamAuth`** / **`ITokenProvider`** (PassThrough, ServerManaged, TokenExchange, None) — no per-tool authentication factory is required when the MCP client supplies tokens (#20).

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
services.AddSingleton<OAuthAuthorizationCodeAuthentication>(sp => {
    return new OAuthAuthorizationCodeAuthentication(
        clientId: "your-client-id",
        authorizationEndpoint: "https://auth.example.com/authorize",
        tokenEndpoint: "https://auth.example.com/token",
        scope: "api_access",
        secureTokenStore: sp.GetRequiredService<ISecureTokenStore>(),
        mcpContextAccessor: sp.GetRequiredService<IMcpContextAccessor>(),
        redirectUri: "http://localhost:5000/auth/callback", // Your app must handle this
        stateSecret: "your-cryptographically-random-secret-at-least-32-chars",
        httpClient: sp.GetRequiredService<IHttpClientFactory>().CreateClient()
    );
});

// Register the Login Tool
services.AddLoginTool(sp => new LoginTool());
```

> **Note:** Pass an `HttpClient` from `IHttpClientFactory` to avoid socket exhaustion. Always provide a `stateSecret` (at least 32 characters) — MCPify will throw if it's missing.

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
});
```

> **Note:** Inbound JWT validation is the host application's responsibility. Use standard ASP.NET Core `AddAuthentication().AddJwtBearer(...)` to validate tokens (expiration, audience, issuer). MCPify's `McpAuthenticationHandler` handles MCP-protocol challenges and Protected Resource Metadata, not JWT signature validation.

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

### Route Protection

When `OAuthConfigurations` are present, MCPify automatically applies `RequireAuthorization` to the MCP route using the MCP authentication scheme. If you use JWT-only authentication (no OAuth configurations), you must apply your own authorization to the MCP route:

```csharp
app.MapMcpifyEndpoint("/mcp")
   .RequireAuthorization("YourJwtPolicy");
```

Per-tool scope enforcement is handled by `SessionAwareToolDecorator` via `ScopeRequirement` metadata on each tool. This works for both HTTP and Stdio transports. For HTTP, scopes are evaluated against `HttpContext.User` claims; for Stdio, scopes are extracted from the JWT access token in the MCP context. Ensure `services.AddAuthorization()` is called to enable scope enforcement.

### Auth Composition (Three Concerns)

MCPify's authentication has three distinct layers:

1. **MCP Challenge & Protected Resource Metadata** — handled by the official `McpAuthenticationHandler` from the MCP SDK. Issues `WWW-Authenticate` challenges pointing to `/.well-known/oauth-protected-resource`.

2. **Inbound JWT Validation** — the host application's responsibility. Use standard ASP.NET Core `AddAuthentication().AddJwtBearer(...)` to validate tokens (expiration, audience, issuer). MCPify does not ship its own JWT validator.

3. **Per-Tool Scope Enforcement** — MCPify's `ScopeRequirementHandler` evaluates `ScopeRequirement` metadata on each tool via `IAuthorizationService`. Requires `services.AddAuthorization()` to be registered.

## Tool Curation

Large OpenAPI specs can generate hundreds of operations — far too many for an agent to use effectively. MCPify provides built-in curation controls to expose a focused, agent-usable toolset.

### Cardinality Caps

Limit the total number of tools across all sources, or per API:

```csharp
builder.Services.AddMcpify(options =>
{
    options.MaxTools = 50; // global cap (default: 100)

    options.ExternalApis.Add(new ExternalApiOptions
    {
        ApiBaseUrl = "https://api.example.com",
        OpenApiUrl = "https://api.example.com/swagger.json",
        MaxTools = 10 // per-API cap (default: unlimited)
    });

    options.LocalEndpoints = new LocalEndpointsOptions
    {
        Enabled = true,
        MaxTools = 15 // per-source cap
    };
});
```

When a cap is exceeded, remaining operations are skipped and a warning is logged.

### Declarative Filters

Use `ToolFilter` for config-friendly allow/deny rules without writing predicates:

```csharp
options.ExternalApis.Add(new ExternalApiOptions
{
    ApiBaseUrl = "https://api.example.com",
    OpenApiUrl = "https://api.example.com/swagger.json",
    ToolFilter = new ToolFilter
    {
        AllowPaths = { "/api/v1" },
        DenyPaths = { "/api/v1/internal" },
        AllowMethods = { "GET", "POST" },
        AllowTags = { "pets", "store" },
        DenyTags = { "admin" },
        AllowOperationIds = { "get_", "create_" },
        ExcludeDeprecated = true
    }
});
```

All matchers use case-insensitive prefix matching. An operation passes if it matches at least one allow rule (or no allow rules are set) and no deny rules.

### Custom Predicate Filter

For advanced filtering, use the `Filter` predicate directly:

```csharp
options.ExternalApis.Add(new ExternalApiOptions
{
    ApiBaseUrl = "https://api.example.com",
    OpenApiUrl = "https://api.example.com/swagger.json",
    Filter = op => !op.Route.Contains("/health") && op.Method == OperationType.Get
});
```

Both `ToolFilter` and `Filter` can be used together — `ToolFilter` is applied first.

### Curation Best Practices

- **Prefer fewer, well-described tools.** Agents struggle with 200+ operations. Aim for 10–30 tools per MCP server.
- **Use `ToolPrefix`** to namespace tools when importing multiple APIs (e.g. `petstore_`, `github_`).
- **Exclude deprecated endpoints** with `ToolFilter.ExcludeDeprecated = true`.
- **Filter by tag** to expose only coherent API groups (e.g. `AllowTags = { "pets" }`).
- **Write good OpenAPI descriptions.** MCPify prefers `description` > `summary` > a structured fallback. The richer your spec, the better the agent understands each tool.

### Why Tools-Only (Not Resources)?

MCPify exposes all OpenAPI operations as **tools**, not MCP resources. This is a deliberate choice:

- **Tools are action-oriented.** Most API operations involve parameters, auth, and side effects — a natural fit for MCP tools.
- **GET endpoints with path parameters** *could* map to `ResourceTemplate`, but query parameters, headers, and content negotiation make the mapping lossy.
- **Agent UX.** A single tool list is simpler for agents to reason about than a mixed resource/tool surface.
- **Future.** Resource template mapping for pure GET-by-id endpoints is tracked for a later preview.

### Local Endpoints: Self-Loopback Design

When `LocalEndpoints.Enabled = true`, MCPify discovers your ASP.NET Core endpoints and creates proxy tools that invoke them via HTTP self-loopback (the app calls its own listening address). This is a deliberate design choice:

- **Full pipeline fidelity.** Self-loopback runs the complete middleware pipeline (auth, validation, serialization, model binding).
- **No fragile fake contexts.** In-process dispatch via constructed `HttpContext` bypasses middleware and breaks auth, CORS, and serialization.
- **Performance.** The loopback is to `localhost` — negligible latency for typical tool call volumes.

Use `BaseUrlOverride` to control the target address when the app listens on multiple URLs or runs behind a proxy.

## Custom Tools

Beyond OpenAPI-imported tools, you can register custom MCP tools with a delegate handler:

```csharp
builder.Services.AddMcpifyTool(
    "search_docs",
    "Search internal documentation by query string",
    async (args, ct) =>
    {
        var query = args?.GetProperty("query").GetString() ?? "";
        var results = await _searchService.SearchAsync(query, ct);
        return JsonSerializer.Serialize(results);
    },
    inputSchema: new
    {
        type = "object",
        properties = new { query = new { type = "string", description = "Search query" } },
        required = new[] { "query" }
    });
```

A simpler overload accepts a string-returning handler. Tools registered this way are automatically wrapped in `SessionAwareToolDecorator` for session context and scope enforcement.

## Security

See [docs/SECURITY.md](docs/SECURITY.md) for the full threat model covering authentication layers, pass-through token safety, SSRF protection, token storage, and hosted deployment checklist.

## Tool Invocation Policies

Register custom policies (rate limiting, allowlists, audit logging) via DI. Policies are evaluated before every tool call — return a `CallToolResult` to deny, or `null` to allow.

```csharp
builder.Services.AddSingleton<IToolInvocationPolicy>(new RateLimitPolicy(maxCalls: 100, TimeSpan.FromMinutes(1)));
builder.Services.AddSingleton<IToolInvocationPolicy>(new ToolAllowlistPolicy(new[] { "api_get_status", "petstore_getPetById" }));
builder.Services.AddSingleton<IToolInvocationPolicy>(new AuditLogPolicy());
```

Built-in policies in `MCPify.Core.Policies`:
- `RateLimitPolicy` — max calls per session per time window
- `ToolAllowlistPolicy` — only permit specified tool names
- `AuditLogPolicy` — log every invocation

Implement `IToolInvocationPolicy` for custom policies (per-client quotas, IP-based rules, etc.).

## Observability

MCPify emits `System.Diagnostics.Metrics` for OpenTelemetry integration:

- `mcpify.tool_calls` (counter) — tags: `tool`, `status`
- `mcpify.tool_duration_ms` (histogram) — tags: `tool`

Structured logging: `MCPify.ToolInvocation` logger records tool name, session hash, status (`ok`/`error`/`scope_denied`/`policy_denied`/`exception`), and elapsed milliseconds. Session IDs are hashed — token values are never logged.

## Comparison

See [docs/COMPARISON.md](docs/COMPARISON.md) for an honest comparison of MCPify vs raw MCP SDK, CLI converters, and FastMCP.

## Contributing

We welcome contributions! Please see our [Contributing Guide](CONTRIBUTING.md) for details.

## License

MIT
