using MCPify.Core;
using MCPify.Core.Auth;
using MCPify.Core.Auth.OAuth;
using MCPify.Core.Session;
using MCPify.Hosting;
using MCPify.Sample.Auth;
using MCPify.Sample.Data;
using MCPify.Tools;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using ModelContextProtocol.AspNetCore.Authentication;
using OpenIddict.Validation.AspNetCore;

namespace MCPify.Sample.Extensions;

public static class DemoServiceExtensions
{
    public static IServiceCollection AddDemoDatabaseAndAuth(this IServiceCollection services, string baseUrl)
    {
        var normalizedBaseUrl = baseUrl.TrimEnd('/');

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseInMemoryDatabase("db");
            options.UseOpenIddict();
        });

        services.AddOpenIddict()
            .AddCore(options =>
            {
                options.UseEntityFrameworkCore()
                       .UseDbContext<ApplicationDbContext>();
            })
            .AddServer(options =>
            {
                options.SetAuthorizationEndpointUris("connect/authorize")
                       .SetTokenEndpointUris("connect/token")
                       .SetConfigurationEndpointUris("connect/internal-openid-configuration");

                options.AllowAuthorizationCodeFlow()
                       .AllowClientCredentialsFlow()
                       .AllowRefreshTokenFlow();
                options.AcceptAnonymousClients();
                options.RequireProofKeyForCodeExchange();
                options.DisableAccessTokenEncryption();
                options.RegisterResources(normalizedBaseUrl, normalizedBaseUrl + "/");

                options.RegisterScopes("read_secrets", "api");

                options.AddDevelopmentEncryptionCertificate()
                       .AddDevelopmentSigningCertificate();

                options.UseAspNetCore()
                       .EnableAuthorizationEndpointPassthrough()
                       .EnableTokenEndpointPassthrough()
                       .DisableTransportSecurityRequirement();
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
            });

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
        });

        services.AddAuthorization();

        services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", builder =>
            {
                builder.AllowAnyOrigin()
                    .AllowAnyMethod()
                    .AllowAnyHeader();
            });
        });

        return services;
    }

    public static IServiceCollection AddDemoSwagger(this IServiceCollection services, string baseUrl)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "MCPify Sample API", Version = "v1" });
            c.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.OAuth2,
                Flows = new OpenApiOAuthFlows
                {
                    AuthorizationCode = new OpenApiOAuthFlow
                    {
                        AuthorizationUrl = new Uri($"{baseUrl}/connect/authorize"),
                        TokenUrl = new Uri($"{baseUrl}/connect/token"),
                        Scopes = new Dictionary<string, string>
                        {
                            { "read_secrets", "Read secrets" }
                        }
                    }
                }
            });
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "oauth2" }
                    },
                    new[] { "read_secrets" }
                }
            });
        });

        return services;
    }

    public static IServiceCollection AddDemoMcpify(this IServiceCollection services, IConfiguration configuration, string baseUrl, string oauthRedirectUri)
    {
        var transport = configuration.GetValue<McpTransportType>("Mcpify:Transport", McpTransportType.Stdio);
        var demoOptions = configuration.GetSection("Demo").Get<DemoOptions>() ?? new DemoOptions();
        var allowFallback = transport == McpTransportType.Stdio;

        services.AddSingleton<OAuthAuthorizationCodeAuthentication>(sp =>
        {
            var secureTokenStore = sp.GetRequiredService<ISecureTokenStore>();
            var mcpContextAccessor = sp.GetRequiredService<IMcpContextAccessor>();
            var sessionMap = sp.GetService<ISessionMap>();
            return new OAuthAuthorizationCodeAuthentication(
                clientId: "demo-client-id",
                authorizationEndpoint: $"{baseUrl}/connect/authorize",
                tokenEndpoint: $"{baseUrl}/connect/token",
                scope: "read_secrets",
                secureTokenStore: secureTokenStore,
                mcpContextAccessor: mcpContextAccessor,
                clientSecret: "demo-client-secret",
                usePkce: true,
                redirectUri: oauthRedirectUri,
                stateSecret: demoOptions.StateSecret,
                allowDefaultSessionFallback: allowFallback,
                sessionMap: sessionMap);
        });

        services.AddLoginTool(sp => new LoginTool());
        services.AddMcpifyTestTool();

        services.AddMcpify(options =>
        {
            options.Transport = transport;
            options.ResourceUrlOverride = baseUrl;
            options.OAuthConfigurations.Add(new OAuth2Configuration
            {
                AuthorizationUrl = $"{baseUrl}/connect/authorize",
                TokenUrl = $"{baseUrl}/connect/token",
                FlowType = "authorization_code",
                AuthorizationServers = new List<string> { baseUrl },
                Scopes = new Dictionary<string, string>
                {
                    ["read_secrets"] = "Read protected secrets"
                }
            });

            // Expose the local API (which is now the "Real" API)
            options.LocalEndpoints = new()
            {
                Enabled = true,
                ToolPrefix = "api_",
                BaseUrlOverride = baseUrl,
                Filter = descriptor =>
                    !descriptor.Route.StartsWith("/connect") && // Hide auth endpoints
                    !descriptor.Route.StartsWith("/auth"),      // Hide callback
                UpstreamAuth = UpstreamAuth.Fallback(
                    UpstreamAuth.PassThrough(),
                    UpstreamAuth.ServerManaged(sp => sp.GetRequiredService<OAuthAuthorizationCodeAuthentication>()))
            };

            // External APIs (Petstore) - Public API, no authentication
            options.ExternalApis.Add(new ExternalApiOptions
            {
                ApiBaseUrl = "https://petstore.swagger.io/v2",
                OpenApiUrl = "https://petstore.swagger.io/v2/swagger.json",
                ToolPrefix = "petstore_",
                UpstreamAuth = UpstreamAuth.None()
            });

            // External APIs (Local File Demo)
            options.ExternalApis.Add(new ExternalApiOptions
            {
                ApiBaseUrl = baseUrl, // Point back to self for demo
                OpenApiFilePath = "sample-api.json",
                ToolPrefix = "localfile_"
            });
        });

        // Accept access tokens using OpenIddict validation, but keep MCP OAuth challenge metadata.
        services.PostConfigure<AuthenticationOptions>(options =>
        {
            options.DefaultAuthenticateScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
        });

        return services;
    }
}
