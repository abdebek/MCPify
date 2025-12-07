# MCPify

**MCPify** is a library that bridges the gap between ASP.NET Core APIs and the **Model Context Protocol (MCP)**. It allows you to effortlessly expose your existing REST endpoints (OpenAPI/Swagger) and internal Minimal APIs as MCP Tools, making them accessible to AI agents like Claude Desktop, Cursor, and others.

## Features

- **OpenAPI Bridge:** Automatically converts any Swagger/OpenAPI specification (JSON/YAML) into MCP Tools.
- **Local Endpoint Bridge:** Automatically discovers and exposes your application's ASP.NET Core Minimal APIs as MCP Tools.
- **Zero-Config Stdio Support:** Built-in support for standard input/output (Stdio) transport, perfect for local integration with AI desktop apps.
- **HTTP (SSE) Support:** Full support for Server-Sent Events (SSE) for remote or multi-client scenarios.
- **Schema Generation:** Automatic JSON schema generation for API parameters and request bodies.
- **Advanced Authentication:**
  - **OAuth 2.0 Authorization Code Flow:** Interactive browser login for local tools.
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

var builder = WebApplication.CreateBuilder(args);

// 1. Add MCPify Services
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
    options.ExternalApis.Add(new()
    {
        SwaggerUrl = "https://api.example.com/swagger.json",
        ApiBaseUrl = "https://api.example.com",
        ToolPrefix = "myapi_",
        Authentication = new OAuthAuthorizationCodeAuthentication(
            clientId: "your-client-id",
            authorizationEndpoint: "https://auth.example.com/authorize",
            tokenEndpoint: "https://auth.example.com/token",
            scope: "read write",
            tokenStore: new FileTokenStore("token.json") // Persist token to disk
        )
    });
});

var app = builder.Build();

// 2. Map your APIs as usual
app.MapGet("/api/users/{id}", (int id) => new { Id = id, Name = "John Doe" });

// 3. Register MCP Tools (Must be called after endpoints are mapped but before Run)
var registrar = app.Services.GetRequiredService<McpifyServiceRegistrar>();
await registrar.RegisterToolsAsync(((IEndpointRouteBuilder)app).DataSources);

// 4. Map the MCP Endpoint
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

3.  **Restart Claude.** Your API endpoints will now appear as tools (e.g., `local_api_users_get`)!

## Configuration

### Transport Modes

- **Stdio (`McpTransportType.Stdio`)**: Default for local tools. Uses Standard Input/Output.
    - *Note:* Console logging is automatically disabled in this mode to prevent protocol corruption.
- **Http (`McpTransportType.Http`)**: Uses Server-Sent Events (SSE).
    - Default endpoints: `/sse` (connection) and `/messages` (requests).

### Local Endpoints

MCPify inspects your application's routing table to generate tools.
- `Enabled`: Set to `true` to enable.
- `ToolPrefix`: A string to prepend to tool names (e.g., "api_").
- `Filter`: A function to select which endpoints to expose.

### External APIs

Proxy external services by providing their OpenAPI spec.
- `SwaggerUrl`: URL to the `swagger.json`.
- `ApiBaseUrl`: The base URL where API requests should be sent.
- `DefaultHeaders`: Custom headers (e.g., Authorization) to include in requests.
- `OpenApiDownloadTimeout`: Configurable timeout for downloading OpenAPI specifications. Defaults to 30 seconds.

#### OpenAPI support
- Built-in provider uses `Microsoft.OpenApi.Readers` and supports Swagger 2.0 and OpenAPI 3.x documents.
- Invalid/unsupported specs are skipped with a warning in logs.
- To use another parser or source, set `options.ProviderOverride` to your own `IOpenApiProvider` implementation (and optionally `options.SchemaGeneratorOverride` for custom JSON schemas).

### Authentication

Secure your external or local endpoints using built-in authentication providers.

#### OAuth 2.0 Authorization Code (Interactive)
Best for local desktop apps (CLI, Claude Desktop). Opens a browser window for the user to log in.

```csharp
Authentication = new OAuthAuthorizationCodeAuthentication(
    clientId: "...",
    authorizationEndpoint: "...",
    tokenEndpoint: "...",
    scope: "...",
    tokenStore: new FileTokenStore("token.json"),
    callbackHost: "localhost", // Optional: Defaults to localhost
    callbackPath: "/callback"  // Optional: Defaults to /callback
)
```

#### OAuth 2.0 Device Flow (Headless)
Best for remote servers or containers. Provides a code to the user to enter on a separate device.

```csharp
Authentication = new DeviceCodeAuthentication(
    clientId: "...",
    deviceCodeEndpoint: "...",
    tokenEndpoint: "...",
    scope: "...",
    tokenStore: new InMemoryTokenStore(),
    userPrompt: (uri, code) => 
    {
        Console.WriteLine($"Please visit {uri} and enter code: {code}");
        return Task.CompletedTask;
    }
)
```

#### Standard Providers
```csharp
// API Key
new ApiKeyAuthentication("api-key", "secret", ApiKeyLocation.Header)

// Bearer Token
new BearerAuthentication("access-token")

// Basic Auth
new BasicAuthentication("username", "password")
```

## Tests

Tests are fully integration-based (no mocks). They spin up in-memory HTTP/OIDC servers to verify:
- Auth code + device code flows (including ID token validation via JWKS).
- Proxy tool path/constraint handling and header forwarding.
- Core authentication providers.

Run them from the repo root:

```bash
dotnet test Tests/MCPify.Tests/MCPify.Tests.csproj
```

## Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## License

This project is licensed under the MIT License.

