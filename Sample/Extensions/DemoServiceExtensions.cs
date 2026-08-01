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
    public static IServiceCollection AddDemoDatabaseAndAuth(this IServiceCollection services, string baseUrl, string? dbName = null)
    {
        var normalizedBaseUrl = baseUrl.TrimEnd('/');

        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseInMemoryDatabase(dbName ?? "db");
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
            options.AddPolicy("DemoCors", builder =>
            {
                builder.SetIsOriginAllowed(origin =>
                        origin.StartsWith("http://localhost", StringComparison.OrdinalIgnoreCase) ||
                        origin.StartsWith("https://localhost", StringComparison.OrdinalIgnoreCase))
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
        var allowClientTokenPassthrough = configuration.GetValue<bool>("Mcpify:AllowClientTokenPassthrough");
        var demoOptions = configuration.GetSection("Demo").Get<DemoOptions>() ?? new DemoOptions();
        var allowFallback = true;
        var serverManagedAuth = UpstreamAuth.ServerManaged(sp => sp.GetRequiredService<OAuthAuthorizationCodeAuthentication>());

        services.AddSingleton<OAuthAuthorizationCodeAuthentication>(sp =>
        {
            var secureTokenStore = sp.GetRequiredService<ISecureTokenStore>();
            var mcpContextAccessor = sp.GetRequiredService<IMcpContextAccessor>();
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
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
                httpClient: httpClientFactory.CreateClient());
        });

        services.AddLoginTool(sp => new LoginTool());

        services.AddMcpify(options =>
        {
            options.Transport = transport;
            options.ResourceUrlOverride = baseUrl;
            options.AllowClientTokenPassthrough = allowClientTokenPassthrough;
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

            options.LocalEndpoints = new()
            {
                Enabled = true,
                ToolPrefix = "api_",
                BaseUrlOverride = baseUrl,
                Filter = descriptor =>
                    !descriptor.Route.StartsWith("/connect") &&
                    !descriptor.Route.StartsWith("/auth"),
                UpstreamAuth = transport == McpTransportType.Stdio
                    ? UpstreamAuth.Fallback(UpstreamAuth.PassThrough(), serverManagedAuth)
                    : allowClientTokenPassthrough
                        ? UpstreamAuth.Fallback(UpstreamAuth.PassThrough(), serverManagedAuth)
                        : serverManagedAuth
            };

            options.ExternalApis.Add(new ExternalApiOptions
            {
                ApiBaseUrl = "https://petstore.swagger.io/v2",
                OpenApiUrl = "https://petstore.swagger.io/v2/swagger.json",
                ToolPrefix = "petstore_",
                UpstreamAuth = UpstreamAuth.None()
            });

            options.ExternalApis.Add(new ExternalApiOptions
            {
                ApiBaseUrl = baseUrl,
                OpenApiFilePath = "sample-api.json",
                ToolPrefix = "localfile_"
            });
        });

        services.PostConfigure<AuthenticationOptions>(options =>
        {
            options.DefaultAuthenticateScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
        });

        return services;
    }
}
