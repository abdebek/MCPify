using MCPify.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Configure MCPify with the Petstore API
builder.Services.AddMcpify(
    swaggerUrl: "https://petstore.swagger.io/v2/swagger.json",
    apiBaseUrl: "https://petstore.swagger.io/v2",
    options =>
    {
        // Add a prefix to all tool names
        options.ToolPrefix = "petstore_";

        // Optional: Filter operations to only include pet-related endpoints
        // options.Filter = op => op.Route.Contains("/pet");
    });

var app = builder.Build();

// Map the MCP endpoint (default path is empty string, which creates /sse and /messages endpoints)
app.MapMcpifyEndpoint();

app.MapGet("/", () => "MCPify Sample - MCP Server is running! Connect via /sse or /messages endpoints.");

app.Run();
