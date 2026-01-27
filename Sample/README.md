# MCPify Sample Application

This sample demonstrates how to use **MCPify** to expose ASP.NET Core endpoints and OpenAPI (Swagger) specifications as tools for the **Model Context Protocol (MCP)**. It showcases a fully functional OAuth 2.0 Authorization Code flow using OpenIddict as an in-app Identity Provider, integrating with MCPify's authentication features.

## What this sample includes:
-   **Local API Endpoints**: Minimal API endpoints (e.g., `/api/users`, `/api/secrets`) exposed as MCP tools.
-   **OAuth 2.0 Provider**: An in-app OAuth 2.0 Authorization Server powered by OpenIddict, demonstrating full auth code and client credentials flows.
-   **Secure Endpoints**: A protected `/api/secrets` endpoint, requiring OAuth 2.0 authorization.
-   **External OpenAPI Integration**: Integration with the public Petstore API (`https://petstore.swagger.io/v2/swagger.json`), exposing its operations as `petstore_` prefixed MCP tools. Also demonstrates loading from a local file (`sample-api.json`) via `localfile_` tools.
-   **MCP Authorization Spec Compliant**: Full compliance with the [MCP Authorization Specification](https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization):
    -   Protected Resource Metadata (`/.well-known/oauth-protected-resource`)
    -   RFC 8707 Resource Parameter in OAuth requests
    -   JWT token validation (expiration, audience, scopes)
    -   403 Forbidden with `insufficient_scope` error
-   **Stdio & HTTP Transports**: Supports both Stdio for local desktop integration and HTTP (SSE) for remote access.

## Prerequisites

-   .NET 8.0, 9.0, or 10.0 SDK

## Getting Started

### 1. Local Integration (Claude Desktop)

The default configuration uses `Stdio` transport, which is designed for local tools.

1.  **Publish the Project:**
    Build the project in Release mode to create the executable DLL (replace `<tfm>` with `net8.0`, `net9.0`, or `net10.0`).
    ```bash
    dotnet publish Sample/MCPify.Sample.csproj -c Release -f <tfm>
    ```

2.  **Configure Claude Desktop:**
    Locate your config file (e.g., `%APPDATA%\Claude\claude_desktop_config.json` on Windows or `~/Library/Application Support/Claude/claude_desktop_config.json` on macOS) and add/update the `mcpServers` entry (replace `<abs-path-to-repo>` with your path):

    ```json
    {
      "mcpServers": {
        "mcpify-sample": {
          "command": "dotnet",
          "args": [
            "<abs-path-to-repo>/Sample/bin/Release/<tfm>/publish/MCPify.Sample.dll"
          ]
        }
      }
    }
    ```
    Replace `<abs-path-to-repo>` with the absolute path to your local `MCPify` repository.

3.  **Restart Claude Desktop.** You should now see the tools (e.g., `petstore_findPetsByStatus`, `api_users_get`, `api_secrets_get`) available.

---

### 2. HTTP / Remote Access

To run the server in HTTP mode (using Server-Sent Events):

1.  **Run with HTTP Flag:**
    ```bash
    cd Sample
    dotnet run -f <tfm> -- --Mcpify:Transport=Http
    ```

2.  **Endpoints:**
    -   **SSE Connection:** `http://localhost:5005/sse`
    -   **Messages:** `http://localhost:5005/messages`
    -   **OAuth Metadata:** `http://localhost:5005/.well-known/oauth-protected-resource`

3.  **Connect a Client:**
    Configure your MCP client to connect to the SSE URL above.

    **Example Claude Config for HTTP:**
    ```json
    {
      "mcpServers": {
        "mcpify-http": {
          "url": "http://localhost:5005/sse"
        }
      }
    }
    ```

### Interactive OAuth 2.0 Authentication

This sample demonstrates how clients can authenticate with MCPify using OAuth 2.0 Authorization Code flow.

1.  **Discover Authentication**: When an unauthenticated client attempts to use a protected tool (e.g., `api_secrets_get`), MCPify will respond with:
    -   `401 Unauthorized` with `WWW-Authenticate: Bearer resource_metadata="..."` for missing/invalid tokens
    -   `403 Forbidden` with `WWW-Authenticate: Bearer error="insufficient_scope", scope="..."` for valid tokens lacking required scopes

    The client should fetch the `resource_metadata` URL to discover authorization server details.
2.  **Initiate Login**: The client (e.g., Claude Desktop) will call the `login_auth_code_pkce` tool provided by MCPify. This tool returns an authorization URL.
3.  **User Authorization**: The user opens the authorization URL in a browser, logs in (using the OpenIddict provider in this sample), and grants consent.
4.  **Callback and Token Exchange**: After user authorization, the browser redirects to MCPify's callback endpoint (`/auth/callback`). MCPify handles the code exchange and stores the token securely for the specific session.
5.  **Access Protected Tools**: The client can then retry the protected tool invocation. MCPify will use the stored token (or a token provided by the client in the `Authorization` header) to authenticate against the backend API.

### Service-to-Service (Client Credentials)
The in-app OpenIddict server already allows the Client Credentials grant. To try it:

1. Swap the authentication factory in `AddDemoMcpify` to use `ClientCredentialsAuthentication` (sample credentials shown below):
    ```csharp
    AuthenticationFactory = sp => new ClientCredentialsAuthentication(
        clientId: "demo-client-id",
        clientSecret: "demo-client-secret",
        tokenEndpoint: $"{baseUrl}/connect/token",
        scope: "read_secrets",
        secureTokenStore: sp.GetRequiredService<ISecureTokenStore>(),
        mcpContextAccessor: sp.GetRequiredService<IMcpContextAccessor>()
    );
    ```
2. Publish/run the sample, then call any `api_` tool. MCPify will fetch and cache a bearer token per MCP session and reuse it until near expiry.

### Device Code Flow (optional)
MCPify supports device code via `DeviceCodeAuthentication`. To experiment with it in the sample:
- Enable device code in OpenIddict (add `.AllowDeviceCodeFlow()` in `AddDemoDatabaseAndAuth`).
- Swap the auth factory to `DeviceCodeAuthentication` (provide the same `connect/token` endpoints and a user prompt callback).
- When a protected tool is called, MCPify will return a verification URL + user code; authorize on another device, then rerun the tool to use the cached token.

### Simple Tokens (API Key / Bearer / Basic)
For quick demos or internal endpoints, wire static credentials instead of OAuth:
```csharp
AuthenticationFactory = sp => new ApiKeyAuthentication("X-API-Key", "secret", ApiKeyLocation.Header);
// or
AuthenticationFactory = sp => new BearerAuthentication("hard-coded-token");
// or
AuthenticationFactory = sp => new BasicAuthentication("user", "password");
```
Attach these either to `options.LocalEndpoints.AuthenticationFactory` or to a specific `ExternalApiOptions.AuthenticationFactory`.

### Token Source Configuration

MCPify now supports flexible token sourcing via the `TokenSource` configuration option. This allows you to control where authentication tokens come from:

#### Server-Managed Authentication (Default)
MCPify handles authentication flows:
```csharp
options.ExternalApis.Add(new ExternalApiOptions
{
    ApiBaseUrl = "https://api.example.com",
    OpenApiUrl = "https://api.example.com/swagger.json",
    TokenSource = TokenSource.Server,  // Default
    AuthenticationFactory = sp => new OAuthAuthorizationCodeAuthentication(...)
});
```

#### Client-Managed Authentication
Let the MCP client provide tokens directly:
```csharp
options.ExternalApis.Add(new ExternalApiOptions
{
    ApiBaseUrl = "https://api.github.com",
    OpenApiUrl = "https://raw.githubusercontent.com/.../api.github.com.json",
    TokenSource = TokenSource.Client  // Client provides tokens
});
```

This is useful when:
- Your MCP client (e.g., Claude Desktop) handles OAuth
- You want to avoid duplicate authentication logic
- Tokens are managed externally

#### Hybrid Approach
Try client token first, fallback to server authentication:
```csharp
options.ExternalApis.Add(new ExternalApiOptions
{
    ApiBaseUrl = "https://api.example.com",
    OpenApiUrl = "https://api.example.com/swagger.json",
    TokenSource = TokenSource.Both,  // Try client, fallback to server
    AuthenticationFactory = sp => new BearerAuthentication("fallback-token")
});
```

#### No Authentication
For public APIs:
```csharp
options.ExternalApis.Add(new ExternalApiOptions
{
    ApiBaseUrl = "https://api.publicapis.org",
    OpenApiUrl = "https://api.publicapis.org/swagger.json",
    TokenSource = TokenSource.None  // No auth
});
```

**Available Values:**
- `TokenSource.Both` (default): Try client first, fallback to server - maximum flexibility
- `TokenSource.Server`: MCPify exclusively manages authentication
- `TokenSource.Client`: MCP client exclusively provides tokens
- `TokenSource.None`: No authentication required

### Pass-through Bearer Tokens
If your MCP client already sends `Authorization: Bearer <token>` to the sample, MCPify will forward that token via `IMcpContextAccessor.AccessToken` when using `TokenSource.Client` or `TokenSource.Both`.

### Protected Resource Metadata

The sample relies on the ASP.NET Core authentication handler from `ModelContextProtocol.AspNetCore`. `AddMcpify` registers the handler and automatically mirrors any OAuth configurations into the protected-resource metadata served to clients.

If you need to tweak what gets advertised (for example to add additional scopes or documentation links), configure `McpAuthenticationOptions` in `Program.cs`:

```csharp
builder.Services.PostConfigure<McpAuthenticationOptions>(options =>
{
  options.ResourceMetadata ??= new ProtectedResourceMetadata();
  options.ResourceMetadata.Documentation = new Uri("https://localhost:5005/docs");
});
```

Ensure the middleware pipeline includes both `app.UseAuthentication();` and `app.UseAuthorization();` so that challenges and metadata responses are handled by the official MCP authentication scheme.

### Relevant configuration knobs
These can be configured in `appsettings.json` or via command-line arguments.

-   `Demo:BaseUrl`: The host/port used by the sample itself (and the auth callback). Defaults to `http://localhost:5005`.
-   `Demo:OAuthRedirectPath`: Path for the callback handler (default `/auth/callback`).
-   `Mcpify:Transport`: `Stdio` or `Http`.
-   `Mcpify:OpenApiDownloadTimeout`: Timeout for downloading OpenAPI specs.
-   `Mcpify:LoginBrowserBehavior`: Controls browser launch for OAuth login. Options: `Auto` (default, detects headless environments), `Always`, or `Never` (ideal for remote/headless servers).

## Troubleshooting

-   **"Waiting for server to respond to initialize request..."**: This usually means you are using `dotnet run` in your MCP client configuration (e.g., VSCode/Claude). `dotnet run` emits build logs ("Building...", "Now listening...") to standard output, which corrupts the JSON-RPC protocol used by Stdio transport.
    -   **Fix:** Point your client command to the **published DLL** (e.g., `dotnet Slampe/bin/Release/<tfm>/publish/MCPify.Sample.dll`) instead of using `dotnet run`. Ensure you have published the project first (`dotnet publish -c Release -f <tfm>`).

-   **Stdio Issues:** If connecting via Stdio fails, ensure no other output is being written to the console. The application automatically disables logging in Stdio mode to prevent this.
-   **Logs:** In Stdio mode, standard logs are suppressed. You can configure file-based logging if debugging is needed.
