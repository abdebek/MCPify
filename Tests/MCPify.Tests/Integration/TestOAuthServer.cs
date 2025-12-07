using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace MCPify.Tests.Integration;

public sealed class TestOAuthServer : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly RsaSecurityKey _signingKey;
    private readonly JsonWebKey _jwk;
    private readonly object _lock = new();
    private readonly string _audience = "client_id";

    private string? _lastAuthCode;
    private string? _lastRefreshToken;
    private string? _lastDeviceCode;
    private bool _deviceAuthorized;

    public string BaseUrl { get; }
    public string AuthorizationEndpoint => $"{BaseUrl}/authorize";
    public string TokenEndpoint => $"{BaseUrl}/token";
    public string DeviceCodeEndpoint => $"{BaseUrl}/device/code";
    public string VerificationEndpoint => $"{BaseUrl}/device/verify";
    public string JwksEndpoint => $"{BaseUrl}/jwks";
    public SecurityKey SigningKey => _signingKey;
    public string Audience => _audience;
    public string Issuer => BaseUrl;

    public TestOAuthServer()
    {
        var port = GetRandomUnusedPort();
        BaseUrl = $"http://localhost:{port}";

        using var rsa = RSA.Create(2048);
        _signingKey = new RsaSecurityKey(rsa.ExportParameters(true)) { KeyId = Guid.NewGuid().ToString("N") };
        _jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(rsa.ExportParameters(false)));
        _jwk.KeyId = _signingKey.KeyId;
        _jwk.Alg = SecurityAlgorithms.RsaSha256;

        _host = Host.CreateDefaultBuilder()
            .ConfigureWebHostDefaults(builder =>
            {
                builder.UseUrls(BaseUrl);
                builder.Configure(ConfigureApp);
            })
            .Build();
    }

    public async Task StartAsync() => await _host.StartAsync();

    public void AuthorizeDevice()
    {
        lock (_lock)
        {
            _deviceAuthorized = true;
        }
    }

    public HttpClient CreateClient() => new() { BaseAddress = new Uri(BaseUrl) };

    private void ConfigureApp(IApplicationBuilder app)
    {
        app.UseRouting();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapGet("/.well-known/openid-configuration", async context =>
            {
                await context.Response.WriteAsJsonAsync(new
                {
                    issuer = BaseUrl,
                    authorization_endpoint = AuthorizationEndpoint,
                    token_endpoint = TokenEndpoint,
                    device_authorization_endpoint = DeviceCodeEndpoint,
                    jwks_uri = JwksEndpoint,
                    response_types_supported = new[] { "code" },
                    grant_types_supported = new[] { "authorization_code", "refresh_token", "urn:ietf:params:oauth:grant-type:device_code" },
                    id_token_signing_alg_values_supported = new[] { SecurityAlgorithms.RsaSha256 },
                    scopes_supported = new[] { "openid", "profile", "read_secrets" }
                });
            });

            endpoints.MapGet("/jwks", async context =>
            {
                await context.Response.WriteAsJsonAsync(new { keys = new[] { _jwk } });
            });

            endpoints.MapGet("/authorize", async context =>
            {
                var redirectUri = context.Request.Query["redirect_uri"];
                var state = context.Request.Query["state"];
                var code = "auth_code_" + Guid.NewGuid();

                lock (_lock)
                {
                    _lastAuthCode = code;
                    _lastRefreshToken = "refresh_" + Guid.NewGuid();
                }

                context.Response.Redirect($"{redirectUri}?code={code}&state={state}");
                await Task.CompletedTask;
            });

            endpoints.MapPost("/token", async context =>
            {
                var form = await context.Request.ReadFormAsync();
                var grantType = form["grant_type"].ToString();

                if (grantType == "authorization_code")
                {
                    if (!string.Equals(form["code"], _lastAuthCode, StringComparison.Ordinal))
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                        await context.Response.WriteAsync("Invalid code");
                        return;
                    }

                    await WriteTokenResponse(context);
                    return;
                }

                if (grantType == "refresh_token")
                {
                    await WriteTokenResponse(context);
                    return;
                }

                if (grantType == "urn:ietf:params:oauth:grant-type:device_code")
                {
                    if (!string.Equals(form["device_code"], _lastDeviceCode, StringComparison.Ordinal))
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                        await context.Response.WriteAsync("{\"error\": \"invalid_device_code\"}");
                        return;
                    }

                    if (!_deviceAuthorized)
                    {
                        context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                        await context.Response.WriteAsync("{\"error\": \"authorization_pending\"}");
                        return;
                    }

                    await WriteTokenResponse(context);
                    return;
                }

                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                await context.Response.WriteAsync("{\"error\": \"unsupported_grant_type\"}");
            });

            endpoints.MapPost("/device/code", async context =>
            {
                var deviceCode = "dc_" + Guid.NewGuid();
                var userCode = "UC-" + Random.Shared.Next(1000, 9999);

                lock (_lock)
                {
                    _lastDeviceCode = deviceCode;
                    _deviceAuthorized = false;
                }

                await context.Response.WriteAsJsonAsync(new
                {
                    device_code = deviceCode,
                    user_code = userCode,
                    verification_uri = VerificationEndpoint,
                    expires_in = 300,
                    interval = 1
                });
            });

            endpoints.MapGet("/device/verify", async context =>
            {
                lock (_lock)
                {
                    _deviceAuthorized = true;
                }

                await context.Response.WriteAsync("Device authorized.");
            });
        });
    }

    private async Task WriteTokenResponse(HttpContext context)
    {
        var accessToken = CreateJwt("test-user");
        var idToken = CreateJwt("test-user", includeIdTokenClaims: true);
        string refreshToken;

        lock (_lock)
        {
            refreshToken = _lastRefreshToken ?? "refresh_" + Guid.NewGuid();
            _lastRefreshToken = refreshToken;
        }

        await context.Response.WriteAsJsonAsync(new
        {
            access_token = accessToken,
            token_type = "Bearer",
            expires_in = 3600,
            refresh_token = refreshToken,
            id_token = idToken
        });
    }

    private string CreateJwt(string subject, bool includeIdTokenClaims = false)
    {
        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, subject),
                new Claim(ClaimTypes.Name, subject)
            }),
            Issuer = BaseUrl,
            Audience = _audience,
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256)
        });

        return handler.WriteToken(token);
    }

    private static int GetRandomUnusedPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}
