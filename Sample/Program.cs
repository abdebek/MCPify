using MCPify.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.AddMcpify(
    swaggerUrl: "https://petstore.swagger.io/v2/swagger.json",
    apiBaseUrl: "https://petstore.swagger.io/v2",
    options =>
    {
        options.ToolPrefix = "petstore_";

        // Optional: Filter operations to only include pet-related endpoints
        // options.Filter = op => op.Route.Contains("/pet");
    });

var app = builder.Build();

app.UseCors("AllowAll");

using (var scope = app.Services.CreateScope())
{
    var initializer = scope.ServiceProvider.GetRequiredService<IMcpifyInitializer>();
    await initializer.InitializeAsync();
}

app.MapMcpifyEndpoint();

app.MapGet("/status", () => "MCPify Sample - MCP Server is running! Connect via /sse endpoint.");

app.Run();