using MCPify.Core;
using MCPify.Hosting;
using MCPify.Sample;
using MCPify.Sample.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = WebApplication.CreateBuilder(args);

// --- Configuration ---
var transport = builder.Configuration.GetValue<McpTransportType>("Mcpify:Transport", McpTransportType.Stdio);
if (transport == McpTransportType.Stdio && !args.Contains("--debug"))
{
    builder.Logging.ClearProviders();
}

builder.Services.Configure<DemoOptions>(builder.Configuration.GetSection("Demo"));
var demoOptions = builder.Configuration.GetSection("Demo").Get<DemoOptions>() ?? new DemoOptions();

var baseUrl = demoOptions.BaseUrl.TrimEnd('/');
var oauthRedirectPath = "/auth/callback"; 
var oauthRedirectUri = $"{baseUrl}{oauthRedirectPath}"; 

builder.WebHost.UseUrls(baseUrl);

// --- Services ---
builder.Services.AddDemoDatabaseAndAuth();
builder.Services.AddDemoSwagger(baseUrl);
builder.Services.AddDemoMcpify(builder.Configuration, baseUrl, oauthRedirectUri);

builder.Services.AddHostedService<Worker>();
builder.Services.AddControllers();

var app = builder.Build();

// --- Pipeline ---
app.UseCors("AllowAll");

app.UseSwagger();
app.UseSwaggerUI();

app.UseMcpifyContext();
app.UseMcpifyOAuth();
app.UseAuthentication();
app.UseAuthorization();

app.MapDemoEndpoints(oauthRedirectPath);

await app.RegisterDemoMcpToolsAsync();

app.MapMcpifyEndpoint();

app.Run();
