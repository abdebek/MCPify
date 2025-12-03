# MCPify

**MCPify** is a library that bridges the gap between ASP.NET Core APIs and the **Model Context Protocol (MCP)**. It allows you to effortlessly expose your existing REST endpoints (OpenAPI/Swagger) and internal Minimal APIs as MCP Tools, making them accessible to AI agents like Claude Desktop, Cursor, and others.

## 🚀 Features

- **OpenAPI Bridge:** Automatically converts any Swagger/OpenAPI specification (JSON/YAML) into MCP Tools.
- **Local Endpoint Bridge:** Automatically discovers and exposes your application's ASP.NET Core Minimal APIs as MCP Tools.
- **Zero-Config Stdio Support:** Built-in support for standard input/output (Stdio) transport, perfect for local integration with AI desktop apps.
- **HTTP (SSE) Support:** Full support for Server-Sent Events (SSE) for remote or multi-client scenarios.
- **Schema Generation:** Automatic JSON schema generation for API parameters and request bodies.

## 📦 Installation

Install the package via NuGet:

```bash
dotnet add package MCPify
```

## 🏁 Quick Start

### 1. Setup in Program.cs

Configure MCPify in your ASP.NET Core application:

```csharp
using MCPify.Core;
using MCPify.Hosting;

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

    // (Optional) Register external APIs via Swagger
    options.ExternalApis.Add(new()
    {
        SwaggerUrl = "https://petstore.swagger.io/v2/swagger.json",
        ApiBaseUrl = "https://petstore.swagger.io/v2",
        ToolPrefix = "petstore_"
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

2.  **Update your Claude config** (`%APPDATA%\Claude\claude_desktop_config.json`):
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

## 📚 Configuration

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

### Authentication

Secure your external or local endpoints using built-in authentication providers.

```csharp
using MCPify.Core.Auth;

// ...

options.ExternalApis.Add(new()
{
    // ...
    Authentication = new ApiKeyAuthentication("api-key", "secret", ApiKeyLocation.Header)
});

// Supported Providers:
// - ApiKeyAuthentication(name, value, location)
// - BearerAuthentication(token)
// - BasicAuthentication(username, password)
```

## 🧪 Tests

To run the unit tests, navigate to the project root and execute the following command:

```bash
dotnet test Tests/MCPify.Tests/MCPify.Tests.csproj
```

The test suite covers:
- Core authentication providers (`ApiKeyAuthentication`, `BearerAuthentication`, `BasicAuthentication`).
- Integration of authentication with the `OpenApiProxyTool`.
- Basic functionality of the `OpenApiProxyTool` (e.g., URL construction).

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request.

## 📄 License

This project is licensed under the MIT License.