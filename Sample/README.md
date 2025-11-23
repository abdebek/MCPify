# MCPify Sample Application

This sample demonstrates how to use MCPify to expose the Swagger Petstore API as an MCP server.

## What This Does

This application:
1. Loads the Swagger Petstore OpenAPI specification from `https://petstore.swagger.io/v2/swagger.json`
2. Dynamically generates MCP tools for each API operation
3. Exposes an MCP server with HTTP transport (SSE) endpoints

## Running the Sample

```bash
cd Sample
dotnet run
```

The application will start and listen on `http://localhost:5000` (or the port shown in the console).

## MCP Endpoints

Once running, the following endpoints are available:

- **`/sse`** - Server-Sent Events endpoint for MCP communication
- **`/messages`** - HTTP messages endpoint for MCP communication
- **`/`** - Simple status page

## Connecting an MCP Client

You can connect any MCP client to this server using the SSE endpoint:

```
http://localhost:5000/sse
```

### Example with Claude Desktop

Add to your Claude Desktop configuration (`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "petstore": {
      "url": "http://localhost:5000/sse"
    }
  }
}
```

## Available Tools

All Petstore API operations are exposed as MCP tools with the prefix `petstore_`:

- `petstore_addPet` - Add a new pet to the store
- `petstore_updatePet` - Update an existing pet
- `petstore_findPetsByStatus` - Finds pets by status
- `petstore_findPetsByTags` - Finds pets by tags
- `petstore_getPetById` - Find pet by ID
- `petstore_deletePet` - Deletes a pet
- And many more...

## Customization

### Filtering Operations

You can filter which operations to expose by uncommenting and modifying the filter in `Program.cs`:

```csharp
options.Filter = op => op.Route.Contains("/pet");
```

### Changing the Prefix

Modify the `ToolPrefix` option:

```csharp
options.ToolPrefix = "myapi_";
```

### Using a Different API

Replace the `swaggerUrl` and `apiBaseUrl` with any OpenAPI/Swagger specification:

```csharp
builder.Services.AddMcpify(
    swaggerUrl: "https://your-api.com/swagger.json",
    apiBaseUrl: "https://your-api.com/api",
    options => { /* ... */ });
```

## Project Structure

```
Sample/
├── Program.cs           - Main application entry point
├── MCPify.Sample.csproj - Project file with MCPify reference
└── README.md            - This file
```

## Learn More

- [MCPify Documentation](../README.md)
- [Model Context Protocol Specification](https://modelcontextprotocol.io/)
- [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk)
