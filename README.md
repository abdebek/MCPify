# MCPify

MCPify is a NuGet package that dynamically loads OpenAPI/Swagger specifications at runtime and exposes each API operation as a tool in an MCP (Model Context Protocol) server.

## Features

- **Dynamic MCP Tools**: Automatically converts OpenAPI operations into MCP tools
- **OpenAPI Version Support**: Supports OpenAPI v2, v3.x, and v3.1+
- **Extensible Architecture**: Provider-based design for easy customization
- **ASP.NET Core Integration**: Minimal and idiomatic integration with ASP.NET Core

## Installation

```bash
dotnet add package MCPify
```

## Quick Start

```csharp
using MCPify.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMcpify(
    swaggerUrl: "https://petstore.swagger.io/v2/swagger.json",
    apiBaseUrl: "https://petstore.swagger.io/v2",
    options =>
    {
        options.ToolPrefix = "pet_";
        // options.Filter = op => op.Route.Contains("/pet");
    });

var app = builder.Build();

app.MapMcpifyEndpoint("/mcp");

app.Run();
```

## Configuration Options

### `McpifyOptions`

- **ToolPrefix**: Optional prefix for all generated tool names
- **Filter**: Optional filter to include only specific operations
- **ProviderOverride**: Custom OpenAPI provider implementation
- **SchemaGeneratorOverride**: Custom JSON Schema generator implementation

## Architecture

MCPify follows a clean, layered architecture:

- **Core**: Configuration and data models
- **OpenApi**: OpenAPI document parsing and operation extraction
- **Schema**: JSON Schema generation for MCP tool definitions
- **Tools**: MCP tool implementations that proxy to REST APIs
- **Hosting**: ASP.NET Core integration extensions

## License

MIT
