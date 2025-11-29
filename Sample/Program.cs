using MCPify.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddLogging();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

builder.Services.AddMcpifyTestTool();

builder.Services.AddMcpify(options =>
{
    options.LocalEndpoints = new()
    {
        Enabled = true,
        ToolPrefix = "local_"
    };

    options.ExternalApis.Add(new()
    {
        SwaggerUrl = "https://petstore.swagger.io/v2/swagger.json",
        ApiBaseUrl = "https://petstore.swagger.io/v2",
        ToolPrefix = "petstore_"
    });

    options.ExternalApis.Add(new()
    {
        SwaggerFilePath = "sample-api.json",
        ApiBaseUrl = "http://localhost:5000",
        ToolPrefix = "file_"
    });
});

var app = builder.Build();

app.Use(async (context, next) =>
{
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {context.Request.Method} {context.Request.Path}");
    await next();
});

app.Use(async (context, next) =>
{
    if (context.Request.Method == "POST" && context.Request.Path == "/sse")
    {
        Console.WriteLine("Patching Request: Rewriting POST /sse -> /messages");
        context.Request.Path = "/messages";
    }
    await next();
});

app.UseCors("AllowAll");

app.MapGet("/api/users/{id}", (int id) => new { Id = id, Name = $"User {id}" });
app.MapPost("/api/users", (UserRequest user) => new { Id = 123, user.Name, user.Email });
app.MapGet("/status", () => "MCPify Sample is Running");

app.MapMcpifyEndpoint();

app.Run();

public record UserRequest(string Name, string Email);