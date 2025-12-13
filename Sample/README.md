# MCPify Sample Application

This sample demonstrates how to use **MCPify** to expose ASP.NET Core endpoints and OpenAPI (Swagger) specifications as tools for the **Model Context Protocol (MCP)**. It showcases a fully functional OAuth 2.0 Authorization Code flow using OpenIddict as an in-app Identity Provider, integrating with MCPify's authentication features.

## What this sample includes:
-   **Local API Endpoints**: Minimal API endpoints (e.g., `/api/users`, `/api/secrets`) exposed as MCP tools.
-   **OAuth 2.0 Provider**: An in-app OAuth 2.0 Authorization Server powered by OpenIddict, demonstrating full auth code and client credentials flows.
-   **Secure Endpoints**: A protected `/api/secrets` endpoint, requiring OAuth 2.0 authorization.
-   **External OpenAPI Integration**: Integration with the public Petstore API (`https://petstore.swagger.io/v2/swagger.json`), exposing its operations as `petstore_` prefixed MCP tools. Also demonstrates loading from a local file (`sample-api.json`) via `localfile_` tools.
-   **Protected Resource Metadata**: Exposes the `/.well-known/oauth-protected-resource` endpoint for client discovery.
-   **Stdio & HTTP Transports**: Supports both Stdio for local desktop integration and HTTP (SSE) for remote access.

## Prerequisites

-   .NET 9.0 SDK

## Getting Started

### 1. Local Integration (Claude Desktop)

The default configuration uses `Stdio` transport, which is designed for local tools.

1.  **Publish the Project:**
    Build the project in Release mode to create the executable DLL.
    ```bash
    dotnet publish Sample/MCPify.Sample.csproj -c Release
    ```

2.  **Configure Claude Desktop:**
    Locate your config file (e.g., `%APPDATA%\Claude\claude_desktop_config.json` on Windows or `~/Library/Application Support/Claude/claude_desktop_config.json` on macOS) and add/update the `mcpServers` entry (replace `<abs-path-to-repo>` with your path):

    ```json
    {
      "mcpServers": {
        "mcpify-sample": {
          "command": "dotnet",
          "args": [
            "<abs-path-to-repo>/Sample/bin/Release/net9.0/publish/MCPify.Sample.dll"
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
    dotnet run --Mcpify:Transport=Http
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

1.  **Discover Authentication**: When an unauthenticated client attempts to use a protected tool (e.g., `api_secrets_get`), MCPify will respond with a `401 Unauthorized` HTTP status code and a `WWW-Authenticate` header, including `resource_metadata_url`. The client should then fetch this metadata.
2.  **Initiate Login**: The client (e.g., Claude Desktop) will call the `login_auth_code_pkce` tool provided by MCPify. This tool returns an authorization URL.
3.  **User Authorization**: The user opens the authorization URL in a browser, logs in (using the OpenIddict provider in this sample), and grants consent.
4.  **Callback and Token Exchange**: After user authorization, the browser redirects to MCPify's callback endpoint (`/auth/callback`). MCPify handles the code exchange and stores the token securely for the specific session.
5.  **Access Protected Tools**: The client can then retry the protected tool invocation. MCPify will use the stored token (or a token provided by the client in the `Authorization` header) to authenticate against the backend API.

### Relevant configuration knobs
These can be configured in `appsettings.json` or via command-line arguments.

-   `Demo:BaseUrl`: The host/port used by the sample itself (and the auth callback). Defaults to `http://localhost:5005`.
-   `Demo:OAuthRedirectPath`: Path for the callback handler (default `/auth/callback`).
-   `Mcpify:Transport`: `Stdio` or `Http`.
-   `Mcpify:OpenApiDownloadTimeout`: Timeout for downloading OpenAPI specs.

## Troubleshooting

-   **Stdio Issues:** If connecting via Stdio fails, ensure no other output is being written to the console. The application automatically disables logging in Stdio mode to prevent this.
-   **Logs:** In Stdio mode, standard logs are suppressed. You can configure file-based logging if debugging is needed.