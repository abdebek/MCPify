# MCPify Sample Application

This sample demonstrates how to use MCPify to expose the Swagger Petstore API as an MCP server.

## What This Does

This application:
1. Loads the Swagger Petstore OpenAPI specification from `https://petstore.swagger.io/v2/swagger.json`
2. Dynamically generates MCP tools for each API operation
3. Exposes an MCP server with HTTP transport (SSE) endpoints

## Running the Sample

To start the server, run the following command in the `Sample` directory:

```bash
cd Sample
dotnet run
````

The application will start and listen on **HTTP port 5000**:

  - URL: `http://localhost:5000`

## MCP Endpoints

Once running, the following endpoints are available:

  - **`/sse`** - Server-Sent Events endpoint for MCP communication
  - **`/messages`** - HTTP messages endpoint for MCP communication
  - **`/status`** - Simple status page to verify the server is running

## Connecting an MCP Client

You can connect any MCP client to this server using the SSE endpoint.

**Connection URL:**

```
http://localhost:5000/sse
```

### Example: Claude Desktop

Add the following to your Claude Desktop configuration (`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "petstore": {
      "url": "http://localhost:5000/sse"
    }
  }
}
```

### Example: VS Code MCP Extension

If you are using a generic MCP extension for VS Code, add this to your configuration file:

```json
{
  "servers": {
    "pets-http-sse": {
      "url": "http://localhost:5000/sse",
      "type": "http"
    }
  },
  "inputs": []
}
```

## Available Tools

All Petstore API operations are exposed as MCP tools. By default, they are prefixed with `petstore_`:

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

Modify the `ToolPrefix` option in `Program.cs`:

```csharp
options.ToolPrefix = "myapi_";
```

### Using a Different API

Replace the `swaggerUrl` and `apiBaseUrl` with any OpenAPI/Swagger specification:

```csharp
builder.Services.AddMcpify(
    swaggerUrl: "[https://your-api.com/swagger.json](https://your-api.com/swagger.json)",
    apiBaseUrl: "[https://your-api.com/api](https://your-api.com/api)",
    options => { /* ... */ });
```


## Learn More

  - [MCPify Documentation](https://www.google.com/search?q=../README.md)
  - [Model Context Protocol Specification](https://modelcontextprotocol.io/)
  - [MCP C\# SDK](https://github.com/modelcontextprotocol/csharp-sdk)
