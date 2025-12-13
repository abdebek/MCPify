# MCPify

**MCPify** is a library that bridges the gap between ASP.NET Core APIs and the **Model Context Protocol (MCP)**. It allows you to effortlessly expose your existing REST endpoints (OpenAPI/Swagger) and internal Minimal APIs as MCP Tools, making them accessible to AI agents like Claude Desktop, Cursor, and others.

## Features

- **OpenAPI Bridge:** Automatically converts any Swagger/OpenAPI specification (JSON/YAML) into MCP Tools.
- **Local Endpoint Bridge:** Automatically discovers and exposes your application's ASP.NET Core Minimal APIs as MCP Tools.
- **RFC 9728 Protected Resource Metadata:** Exposes a standard `/.well-known/oauth-protected-resource` endpoint for OAuth 2.0 clients to dynamically discover authentication requirements.
- **Zero-Config Stdio Support:** Built-in support for standard input/output (Stdio) transport, perfect for local integration with AI desktop apps.
- **HTTP (SSE) Support:** Full support for Server-Sent Events (SSE) for remote or multi-client scenarios.
- **Schema Generation:** Automatic JSON schema generation for API parameters and request bodies.
- **Advanced Authentication:**
  - **OAuth 2.0 Authorization Code Flow (managed & pass-through):** Supports interactive browser login with per-session token isolation using `ISecureTokenStore`. Also allows clients to pass their own OAuth tokens directly to MCPify, which are then forwarded to backend APIs.
  - **OAuth 2.0 Device Code Flow:** Headless login for remote/containerized servers.
  - **Standard Auth:** API Key, Bearer Token, Basic Auth.

## Installation

Install the package via NuGet:

```bash
dotnet add package MCPify
```

## Quick Start

### 1. Setup in Program.cs

Configure MCPify in your ASP.NET Core application:

```csharp
using MCPify.Core;
using MCPify.Hosting;
using MCPify.Core.Auth;
using MCPify.Core.Auth.OAuth;
using Microsoft.AspNetCore.Routing; // Required for IEndpointRouteBuilder

var builder = WebApplication.CreateBuilder(args);

// ... your other services ...

builder.Services.AddMcpify(options =>
{
    // Choose Transport (Stdio for local tools, Http for remote)
    options.Transport = McpTransportType.Stdio;
    
    // Enable automatic discovery of local Minimal API endpoints
    options.LocalEndpoints = new()
    {
        Enabled = true,
        ToolPrefix = "local_" // Prefix for generated tools (e.g., local_get_user)
    };

    // (Optional) Register external APIs via Swagger with OAuth2
    // OAuth configuration will be automatically parsed from SwaggerUrl
    options.ExternalApis.Add(new()
    {
        SwaggerUrl = "https://petstore.swagger.io/v2/swagger.json",
        ApiBaseUrl = "https://petstore.swagger.io/v2",
        ToolPrefix = "petstore_",
        // If the external API requires authentication, you can configure it here.
        // For OAuth Authorization Code flow, MCPify will manage tokens if no incoming token is provided.
        AuthenticationFactory = sp => new OAuthAuthorizationCodeAuthentication(
            clientId: "your-client-id", // Your OAuth client ID for Petstore
            authorizationEndpoint: "https://petstore.swagger.io/oauth/dialog", // Example authorization endpoint
            tokenEndpoint: "https://petstore.swagger.io/oauth/token", // Example token endpoint
            scope: "write:pets read:pets", // Required scopes
            secureTokenStore: sp.GetRequiredService<ISecureTokenStore>(), // MCPify's secure token store
            mcpContextAccessor: sp.GetRequiredService<IMcpContextAccessor>(), // Accessor for current MCP context
            redirectUri: "http://localhost:5000/auth/callback" // Your app's callback URI
        )
    });
});

var app = builder.Build();

// ... your other middleware (e.g., app.UseRouting(), app.UseAuthentication(), app.UseAuthorization()) ...

// Enable MCPify's context accessor middleware
app.UseMcpifyContext();
// Enable MCPify's OAuth authentication middleware for challenging clients
app.UseMcpifyOAuth();

// Map your APIs as usual (e.g., Minimal APIs)
app.MapGet("/api/hello", () => "Hello MCPify!");

// Register MCP Tools (Must be called after endpoints are mapped but before Run)
// This should typically be awaited if app.Run is async.
var registrar = app.Services.GetRequiredService<McpifyServiceRegistrar>();
await registrar.RegisterToolsAsync(((IEndpointRouteBuilder)app).DataSources);

// Map the MCP Endpoint (for HTTP transport) and OAuth metadata endpoint
app.MapMcpifyEndpoint(); 

app.Run();
```

### 2. Connect with Claude Desktop

To use your app as a local tool in Claude Desktop:

1.  **Publish your app** to a single executable or DLL.
    ```bash
    dotnet publish -c Release
    ```

2.  **Update your Claude config** (`%APPDATA%\Claude\claude_desktop_config.json` on Windows, `~/Library/Application Support/Claude/claude_desktop_config.json` on Mac):
    ```json
    {
      "mcpServers": {
        "my-api": {
          "command": "dotnet",
          "args": [
            "C:/Path/To/YourApp/bin/Release/net9.0/publish/YourApp.dll"
          ]
        }
      }
    }
    ```

3.  **Restart Claude.** Your API endpoints will now appear as tools!

## Configuration

### Transport Modes

- **Stdio (`McpTransportType.Stdio`)**: Default for local tools. Uses Standard Input/Output.
    - *Note:* Console logging is automatically disabled in this mode to prevent protocol corruption.
- **Http (`McpTransportType.Http`)**: Uses Server-Sent Events (SSE) and exposes a `/.well-known/oauth-protected-resource` endpoint.
    - Default endpoints: `/sse` (connection), `/messages` (requests), `/.well-known/oauth-protected-resource`.

### Local Endpoints

MCPify inspects your application's routing table to generate tools.
- `Enabled`: Set to `true` to enable.
- `ToolPrefix`: A string to prepend to tool names (e.g., "api_").
- `Filter`: A function to select which endpoints to expose.
- `AuthenticationFactory`: A factory to provide an `IAuthenticationProvider` for these local tools.

### External APIs

Proxy external services by providing their OpenAPI spec.
- `SwaggerUrl`: URL to the `swagger.json`.
- `ApiBaseUrl`: The base URL where API requests should be sent.
- `ToolPrefix`: A string to prepend to tool names (e.g., "myapi_").
- `DefaultHeaders`: Custom headers (e.g., Authorization) to include in requests.
- `OpenApiDownloadTimeout`: Configurable timeout for downloading OpenAPI specifications. Defaults to 30 seconds.
- `AuthenticationFactory`: A factory to provide an `IAuthenticationProvider` for tools from this external API.

#### OpenAPI support
- Built-in provider uses `Microsoft.OpenApi.Readers` and supports Swagger 2.0 and OpenAPI 3.0/3.1 documents.
- Invalid/unsupported specs fail fast with an exception that lists parsing errors.
- To use another parser or source, set `options.ProviderOverride` to your own `IOpenApiProvider` implementation (and optionally `options.SchemaGeneratorOverride` for custom JSON schemas).
- 3.1 compatibility: if your spec is 3.1, MCPify will down-convert known 3.1-only constructs (e.g., `type: ["string","null"]`, numeric `exclusiveMinimum/Maximum`, `const`, `examples`, `jsonSchemaDialect`, `webhooks`) to a 3.0.3-compatible shape before parsing.

### Authentication

Secure your external or local endpoints using built-in authentication providers. MCPify now supports both managed authentication (where MCPify handles token storage and refresh) and pass-through authentication (where the client provides the token).

#### OAuth 2.0 Authorization Code (Interactive)
Best for local desktop apps (CLI, Claude Desktop). MCPify's built-in `LoginTool` facilitates this interactive flow. Tokens are isolated per session using `ISecureTokenStore`.

```csharp
AuthenticationFactory = sp => new OAuthAuthorizationCodeAuthentication(
    clientId: "your-client-id",
    authorizationEndpoint: "https://auth.example.com/authorize",
    tokenEndpoint: "https://auth.example.com/token",
    scope: "read write",
    secureTokenStore: sp.GetRequiredService<ISecureTokenStore>(), // Use the registered secure token store
    mcpContextAccessor: sp.GetRequiredService<IMcpContextAccessor>(), // Accessor for current MCP context
    redirectUri: "http://localhost:5000/auth/callback", // Your app's callback URI
    usePkce: true // Recommended for public clients
)

// Ensure your app maps the callback endpoint, e.g., app.MapAuthCallback("/auth/callback");
```
Experimental desktop workflow note: most MCP clients do not automatically capture the browser redirect yet. Users typically copy the code/token from the browser page back into the MCP client. The `LoginTool` and callbacks are wired, but a smooth handoff depends on client support for the required arguments—treat this as experimental until mcp clients fully supports the flow.

#### OAuth 2.0 Device Flow (Headless)
Best for remote servers or containers. MCPify's `LoginTool` can also initiate this flow, providing a code for the user to enter on a separate device.

```csharp
AuthenticationFactory = sp => new DeviceCodeAuthentication(
    clientId: "your-client-id",
    deviceCodeEndpoint: "https://auth.example.com/device",
    tokenEndpoint: "https://auth.example.com/token",
    scope: "read write",
    secureTokenStore: sp.GetRequiredService<ISecureTokenStore>(),
    mcpContextAccessor: sp.GetRequiredService<IMcpContextAccessor>(),
    userPrompt: (verificationUri, userCode) => 
    {
        Console.WriteLine($"Please visit {verificationUri} and enter code: {userCode}");
    return Task.CompletedTask;
}
)
```

#### OAuth 2.0 Client Credentials (Service-to-Service)
Use when no user is present and you just need an app token. Tokens are stored per MCP session using `ISecureTokenStore` to avoid re-fetching on every call.

```csharp
AuthenticationFactory = sp => new ClientCredentialsAuthentication(
    clientId: "service-client-id",
    clientSecret: "service-secret",
    tokenEndpoint: "https://auth.example.com/token",
    scope: "read write",
    secureTokenStore: sp.GetRequiredService<ISecureTokenStore>(),
    mcpContextAccessor: sp.GetRequiredService<IMcpContextAccessor>()
);
```

#### Secure Token Storage (`ISecureTokenStore`)
MCPify provides `EncryptedFileTokenStore` for secure, file-based persistence of authentication tokens. On Windows, it uses DPAPI. On other platforms, it falls back to AES encryption with a key derived from `MCPIFY_TOKENSTORE_KEY` (if set), a generated per-installation key file, or an optional key passed to the constructor. It is automatically registered when you call `builder.Services.AddMcpify()`, but you can replace it with your own `ISecureTokenStore` if you want to centralize key management (e.g., KMS).

#### Pass-Through Bearer Tokens
If a client provides an `Authorization: Bearer <token>` header to MCPify, this token will automatically be made available to `IAuthenticationProvider` implementations via `IMcpContextAccessor.AccessToken`. The `OAuthAuthorizationCodeAuthentication` implementation will prioritize this pass-through token over any tokens it might have stored internally.

#### Standard Providers
These can be used when simple, static tokens are sufficient.
```csharp
// API Key
new ApiKeyAuthentication("api-key", "secret", ApiKeyLocation.Header)

// Bearer Token
new BearerAuthentication("access-token")

// Basic Auth
new BasicAuthentication("username", "password")
```
These are easiest to wire for testing or internal services; you can also set `AuthenticationFactory` per external API or local endpoint group to plug them in.

## Tests

Tests are fully integration-based (no mocks). They spin up in-memory HTTP/OIDC servers to verify:
-   Auth code + device code flows (including ID token validation via JWKS).
-   OAuth 2.0 Protected Resource Metadata endpoint (`/.well-known/oauth-protected-resource`).
-   Authentication middleware challenging clients with `WWW-Authenticate` headers.
-   Proxy tool path/constraint handling and header forwarding.
-   Core authentication providers, including token storage and pass-through.

Run them from the repo root:

```bash
dotnet test Tests/MCPify.Tests/MCPify.Tests.csproj
```

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License.
