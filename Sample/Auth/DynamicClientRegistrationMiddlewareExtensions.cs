using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenIddict.Abstractions;

namespace MCPify.Sample.Auth;

public static class DynamicClientRegistrationMiddlewareExtensions
{
    private static readonly string[] SupportedTokenEndpointAuthMethods =
    [
        "client_secret_basic",
        "client_secret_post"
    ];

    public static IApplicationBuilder UseDynamicClientRegistration(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            if (HttpMethods.IsGet(context.Request.Method) &&
                context.Request.Path.Equals("/.well-known/openid-configuration", StringComparison.OrdinalIgnoreCase))
            {
                await WriteDiscoveryDocumentAsync(context);
                return;
            }

            if (HttpMethods.IsPost(context.Request.Method) &&
                context.Request.Path.Equals("/connect/register", StringComparison.OrdinalIgnoreCase))
            {
                await HandleRegistrationAsync(context);
                return;
            }

            await next();
        });
    }

    private static async Task WriteDiscoveryDocumentAsync(HttpContext context)
    {
        var authority = $"{context.Request.Scheme}://{context.Request.Host}";
        var issuer = authority + "/";

        await WriteJsonAsync(context, StatusCodes.Status200OK, new
        {
            issuer,
            authorization_endpoint = $"{authority}/connect/authorize",
            token_endpoint = $"{authority}/connect/token",
            registration_endpoint = $"{authority}/connect/register",
            jwks_uri = $"{authority}/.well-known/jwks",
            grant_types_supported = new[] { "authorization_code", "refresh_token", "client_credentials" },
            response_types_supported = new[] { "code" },
            response_modes_supported = new[] { "query", "form_post", "fragment" },
            scopes_supported = new[] { "openid", "offline_access", "read_secrets", "api" },
            subject_types_supported = new[] { "public" },
            id_token_signing_alg_values_supported = new[] { "RS256" },
            code_challenge_methods_supported = new[] { "S256", "plain" },
            token_endpoint_auth_methods_supported = SupportedTokenEndpointAuthMethods,
            authorization_response_iss_parameter_supported = true
        });
    }

    private static async Task HandleRegistrationAsync(HttpContext context)
    {
        DynamicClientRegistrationRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<DynamicClientRegistrationRequest>(
                context.Request.Body,
                JsonSerializerOptions.Web,
                context.RequestAborted);
        }
        catch (JsonException)
        {
            await WriteJsonAsync(context, StatusCodes.Status400BadRequest, new
            {
                error = "invalid_client_metadata",
                error_description = "Request body must be valid JSON."
            });
            return;
        }

        request ??= new DynamicClientRegistrationRequest();

        if (request.RedirectUris.Count == 0)
        {
            await WriteJsonAsync(context, StatusCodes.Status400BadRequest, new
            {
                error = "invalid_redirect_uri",
                error_description = "At least one redirect URI is required."
            });
            return;
        }

        var redirectUris = new List<Uri>();
        foreach (var value in request.RedirectUris)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            {
                await WriteJsonAsync(context, StatusCodes.Status400BadRequest, new
                {
                    error = "invalid_redirect_uri",
                    error_description = $"Invalid redirect URI: '{value}'."
                });
                return;
            }

            redirectUris.Add(uri);
        }

        var tokenEndpointAuthMethod = string.IsNullOrWhiteSpace(request.TokenEndpointAuthMethod)
            ? "client_secret_basic"
            : request.TokenEndpointAuthMethod.Trim();

        if (string.Equals(tokenEndpointAuthMethod, "none", StringComparison.Ordinal))
        {
            tokenEndpointAuthMethod = "client_secret_basic";
        }

        if (!SupportedTokenEndpointAuthMethods.Contains(tokenEndpointAuthMethod, StringComparer.Ordinal))
        {
            await WriteJsonAsync(context, StatusCodes.Status400BadRequest, new
            {
                error = "invalid_client_metadata",
                error_description = $"Unsupported token_endpoint_auth_method '{tokenEndpointAuthMethod}'."
            });
            return;
        }

        var grantTypes = request.GrantTypes.Count > 0
            ? request.GrantTypes
            : ["authorization_code", "refresh_token"];

        var responseTypes = request.ResponseTypes.Count > 0
            ? request.ResponseTypes
            : ["code"];

        if (responseTypes.Any(type => !string.Equals(type, "code", StringComparison.Ordinal)))
        {
            await WriteJsonAsync(context, StatusCodes.Status400BadRequest, new
            {
                error = "invalid_client_metadata",
                error_description = "Only response type 'code' is supported."
            });
            return;
        }

        var clientSecret = GenerateClientSecret();

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = $"mcpify_{Guid.NewGuid():N}",
            DisplayName = string.IsNullOrWhiteSpace(request.ClientName)
                ? "Dynamic MCP Client"
                : request.ClientName.Trim()
        };

        descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Authorization);
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.Code);
        var authority = $"{context.Request.Scheme}://{context.Request.Host}".TrimEnd('/');
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Resource + authority);
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Resource + authority + "/");

        descriptor.ClientType = OpenIddictConstants.ClientTypes.Confidential;
        descriptor.ClientSecret = clientSecret;

        foreach (var grantType in grantTypes)
        {
            switch (grantType)
            {
                case "authorization_code":
                    descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode);
                    break;
                case "refresh_token":
                    descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.RefreshToken);
                    break;
                case "client_credentials":
                    descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.ClientCredentials);
                    break;
                default:
                    await WriteJsonAsync(context, StatusCodes.Status400BadRequest, new
                    {
                        error = "invalid_client_metadata",
                        error_description = $"Unsupported grant type '{grantType}'."
                    });
                    return;
            }
        }

        var scopes = request.Scope
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (scopes.Count == 0)
        {
            scopes.Add("openid");
            scopes.Add("read_secrets");
        }

        foreach (var scope in scopes)
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + scope);
        }

        foreach (var redirectUri in redirectUris)
        {
            descriptor.RedirectUris.Add(redirectUri);
        }

        var manager = context.RequestServices.GetRequiredService<IOpenIddictApplicationManager>();
        await manager.CreateAsync(descriptor, context.RequestAborted);

        await WriteJsonAsync(context, StatusCodes.Status201Created, new
        {
            client_id = descriptor.ClientId,
            client_secret = clientSecret,
            client_id_issued_at = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            client_secret_expires_at = 0,
            redirect_uris = redirectUris.Select(uri => uri.AbsoluteUri).ToArray(),
            grant_types = grantTypes,
            response_types = responseTypes,
            token_endpoint_auth_method = tokenEndpointAuthMethod
        });
    }

    private static async Task WriteJsonAsync(HttpContext context, int statusCode, object payload)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }

    private static string GenerateClientSecret()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

public sealed class DynamicClientRegistrationRequest
{
    [JsonPropertyName("client_name")]
    public string? ClientName { get; set; }

    [JsonPropertyName("token_endpoint_auth_method")]
    public string TokenEndpointAuthMethod { get; set; } = "client_secret_basic";

    [JsonPropertyName("redirect_uris")]
    public List<string> RedirectUris { get; set; } = [];

    [JsonPropertyName("grant_types")]
    public List<string> GrantTypes { get; set; } = [];

    [JsonPropertyName("response_types")]
    public List<string> ResponseTypes { get; set; } = [];

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = string.Empty;
}
