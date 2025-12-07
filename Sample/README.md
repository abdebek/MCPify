# MCPify Sample Application

This sample demonstrates how to use **MCPify** to expose ASP.NET Core endpoints and OpenAPI (Swagger) specifications as tools for the **Model Context Protocol (MCP)**.

What this sample includes:
- Minimal API endpoints exposed as MCP tools.
- An optional in-app OAuth/OIDC provider (authorize/token/device code) for demonstrating auth flows end to end (opt-in).
- A generated `mock-api.json` OpenAPI document to showcase external API bridging when OAuth demo is enabled (ignored by git).

It supports two modes of operation:
1. **Stdio:** For local integration with clients like **Claude Desktop**.
2. **HTTP (SSE):** For remote access or multi-client scenarios.

## Prerequisites

- .NET 9.0 SDK

## Getting Started

### 1. Local Integration (Claude Desktop)

The default configuration uses `Stdio` transport, which is designed for local tools.

1. **Publish the Project:**
   Build the project in Release mode to create the executable DLL.
   ```bash
   dotnet publish Sample/MCPify.Sample.csproj -c Release
   ```

2. **Configure Claude Desktop:**
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
   *Note: Replace `D:/C/repos/MCPify` with the absolute path to your local `MCPify` repository.*

3. **Restart Claude Desktop.** You should see the tools (e.g., `petstore_findPetsByStatus`, `local_api_users_get`, `local_status_get`) available.

---

### 2. HTTP / Remote Access

To run the server in HTTP mode (using Server-Sent Events):

1. **Run with HTTP Flag:**
   ```bash
   cd Sample
   dotnet run --Mcpify:Transport=Http
   ```

2. **Endpoints:**
   - **SSE Connection:** `http://localhost:5000/sse`
   - **Messages:** `http://localhost:5000/messages`

3. **Connect a Client:**
   Configure your MCP client to connect to the SSE URL above.

   **Example Claude Config for HTTP:**
   ```json
   {
     "mcpServers": {
       "mcpify-http": {
         "url": "http://localhost:5000/sse"
       }
     }
   }
   ```

### Choose your demo level

- **Simplest (local endpoints only):** Run `dotnet run` (or published binary) with default settings. This exposes minimal APIs as tools and skips the OAuth demo.
- **OAuth/OIDC demo enabled:** Pass `--Demo:EnableOAuth=true` (or set in `appsettings.json`). This spins up the mock OAuth provider, generates `mock-api.json`, and registers `secure_` tools that exercise auth code flow end to end.

## Using MCPify as a NuGet Package

When integrating MCPify into your own ASP.NET Core application as a NuGet package, the setup involves configuring your host application rather than running the `MCPify.Sample` project directly.

### Referencing the Package
First, add the MCPify NuGet package to your ASP.NET Core project:
```xml
<ItemGroup>
  <PackageReference Include="MCPify" Version="[LatestVersion]" />
</ItemGroup>
```
Or via the command line:
```bash
dotnet add package MCPify
```

### Stdio Integration (for Local Clients like Claude Desktop)

For Stdio transport, Claude Desktop (or any MCP client) will launch *your* application's executable (`.dll`). Your application then uses MCPify internally.

1.  **Configure your `Program.cs`:**
    Ensure your application's `Program.cs` configures MCPify services and sets the transport to `Stdio`, similar to the `MCPify.Sample` project:

    ```csharp
    // In your main application's Program.cs
    var builder = WebApplication.CreateBuilder(args);
    // ... other services ...

    builder.Services.AddMcpify(options =>
    {
        options.Transport = McpTransportType.Stdio; // Set to Stdio
        // ... configure local endpoints and external APIs as needed ...
    });

    var app = builder.Build();
    // ... map your application's endpoints ...

    // Register MCPify tools after your app's endpoints are mapped
    var registrar = app.Services.GetRequiredService<McpifyServiceRegistrar>();
    await registrar.RegisterToolsAsync(((IEndpointRouteBuilder)app).DataSources);

    app.MapMcpifyEndpoint(); // This will enable Stdio transport for MCP
    app.Run();
    ```

2.  **Configure Claude Desktop:**
    In your `claude_desktop_config.json`, the `command` and `args` should point to *your application's* published `.dll`:

    ```json
    {
      "mcpServers": {
        "my-app-name": {
          "command": "dotnet",
          "args": [
            "/path/to/YourApp/bin/Release/net9.0/publish/YourApp.dll"
          ]
        }
      }
    }
    ```
    Replace `/path/to/YourApp/bin/Release/net9.0/publish/YourApp.dll` with the actual path to your application's published DLL.

### HTTP Integration (for Remote Clients or Web-based MCP)

For HTTP transport, your application will host the MCP endpoints accessible over a network.

1.  **Configure your `Program.cs`:**
    Set the transport to `Http` and ensure the application runs as a web server:

    ```csharp
    // In your main application's Program.cs
    var builder = WebApplication.CreateBuilder(args);
    // ... other services ...

    builder.Services.AddMcpify(options =>
    {
        options.Transport = McpTransportType.Http; // Set to Http
        // ... configure local endpoints and external APIs as needed ...
    });

    var app = builder.Build();
    // ... map your application's endpoints ...

    // Register MCPify tools after your app's endpoints are mapped
    var registrar = app.Services.GetRequiredService<McpifyServiceRegistrar>();
    await registrar.RegisterToolsAsync(((IEndpointRouteBuilder)app).DataSources);

    app.MapMcpifyEndpoint(); // This will map the /sse and /messages HTTP endpoints
    app.Run(); // Your app will start as a web server
    ```

2.  **Configure Claude Desktop (or other MCP client):**
    Point your client to the URL where your application is running and serving the MCP endpoints (e.g., `http://localhost:5000/sse`):

    ```json
    {
      "mcpServers": {
        "my-app-name-http": {
          "url": "http://localhost:5000/sse"
        }
      }
    }
    ```
    Adjust the URL as per your application's configuration.

## Configuration

You can configure the transport and other settings in `appsettings.json` or via command-line arguments.

**appsettings.json:**
```json
{
  "Mcpify": {
    "Transport": "Stdio", // or "Http"
    "OpenApiDownloadTimeout": "00:00:45" // 45 seconds (default is 30s)
  }
}
```

**Command Line:**
```bash
dotnet run --Mcpify:Transport=Http --Mcpify:OpenApiDownloadTimeout=00:00:45
```

## Features

- **OpenAPI Bridge:** Automatically converts Swagger/OpenAPI definitions (e.g., Petstore) into MCP Tools.
- **Local Endpoint Bridge:** Automatically exposes your ASP.NET Core Minimal APIs as MCP Tools.
- **Transport Agnostic:** Switch between Stdio and HTTP with a simple config change.
- **Authentication (JWT Bearer)**: The sample includes a JWT Bearer authentication setup.
  - A secure endpoint `/api/secure` is protected using `.RequireAuthorization()`.
  - At startup, a demo JWT token is generated and printed to the console.
  - **MCPify is configured to use this demo token** for all local endpoints by setting `options.LocalEndpoints.Authentication = new BearerAuthentication(demoToken);`.
  - This means AI agents calling local tools will be automatically authenticated.

## Troubleshooting

- **Stdio Issues:** If connecting via Stdio fails, ensure no other output is being written to the console. The application automatically disables logging in Stdio mode to prevent this, but ensure no `Console.WriteLine` calls exist in your own startup code.
- **Logs:** In Stdio mode, standard logs are suppressed. You can configure file-based logging if debugging is needed.
- **Generated files:** The sample writes a `mock-api.json` (OpenAPI document) and `demo_token.json` (token cache) to disk; both are ignored by git and can be safely deleted between runs.
