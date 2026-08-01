using MCPify.Core;
using MCPify.Hosting;
using MCPify.Sample;
using MCPify.Sample.Auth;
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
builder.Services.AddDemoDatabaseAndAuth(baseUrl);
builder.Services.AddDemoSwagger(baseUrl);
builder.Services.AddDemoMcpify(builder.Configuration, baseUrl, oauthRedirectUri);

builder.Services.AddHostedService<Worker>();
builder.Services.AddControllers();

var app = builder.Build();

// --- Pipeline ---
app.UseCors("DemoCors");

app.UseSwagger();
app.UseSwaggerUI();

// Expose OIDC discovery + dynamic client registration before auth middleware.
app.UseDynamicClientRegistration();

app.UseAuthentication();
app.UseAuthorization();

app.MapDemoEndpoints(oauthRedirectPath);

app.MapMcpifyEndpoint("/mcp");

app.Run();

public partial class Program { }
