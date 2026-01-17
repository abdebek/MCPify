using System.Text;
using System.Text.Json;
using MCPify.Core.Auth;

namespace MCPify.Tests.Unit;

public class JwtAccessTokenValidatorTests
{
    private static string CreateJwt(object payload)
    {
        var header = new { alg = "HS256", typ = "JWT" };
        var headerJson = JsonSerializer.Serialize(header);
        var payloadJson = JsonSerializer.Serialize(payload);

        var headerB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

        // Signature is not validated by JwtAccessTokenValidator, so we can use a dummy
        var signatureB64 = Base64UrlEncode(Encoding.UTF8.GetBytes("dummy-signature"));

        return $"{headerB64}.{payloadB64}.{signatureB64}";
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    [Fact]
    public async Task ValidateAsync_ExtractsScopesFromString()
    {
        var options = new TokenValidationOptions();
        var validator = new JwtAccessTokenValidator(options);

        var token = CreateJwt(new
        {
            sub = "user123",
            scope = "read write admin",
            exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        });

        var result = await validator.ValidateAsync(token, null);

        Assert.True(result.IsValid);
        Assert.Equal(3, result.Scopes.Count);
        Assert.Contains("read", result.Scopes);
        Assert.Contains("write", result.Scopes);
        Assert.Contains("admin", result.Scopes);
    }

    [Fact]
    public async Task ValidateAsync_ExtractsScopesFromArray()
    {
        var options = new TokenValidationOptions();
        var validator = new JwtAccessTokenValidator(options);

        var token = CreateJwt(new
        {
            sub = "user123",
            scope = new[] { "read", "write", "admin" },
            exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        });

        var result = await validator.ValidateAsync(token, null);

        Assert.True(result.IsValid);
        Assert.Equal(3, result.Scopes.Count);
        Assert.Contains("read", result.Scopes);
        Assert.Contains("write", result.Scopes);
        Assert.Contains("admin", result.Scopes);
    }

    [Fact]
    public async Task ValidateAsync_ExtractsScopesFromScpClaim()
    {
        var options = new TokenValidationOptions();
        var validator = new JwtAccessTokenValidator(options);

        var token = CreateJwt(new
        {
            sub = "user123",
            scp = "read write",
            exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        });

        var result = await validator.ValidateAsync(token, null);

        Assert.True(result.IsValid);
        Assert.Equal(2, result.Scopes.Count);
        Assert.Contains("read", result.Scopes);
        Assert.Contains("write", result.Scopes);
    }

    [Fact]
    public async Task ValidateAsync_ExtractsAudienceFromString()
    {
        var options = new TokenValidationOptions();
        var validator = new JwtAccessTokenValidator(options);

        var token = CreateJwt(new
        {
            sub = "user123",
            aud = "https://api.example.com",
            exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        });

        var result = await validator.ValidateAsync(token, null);

        Assert.True(result.IsValid);
        Assert.Single(result.Audiences);
        Assert.Equal("https://api.example.com", result.Audiences[0]);
    }

    [Fact]
    public async Task ValidateAsync_ExtractsAudienceFromArray()
    {
        var options = new TokenValidationOptions();
        var validator = new JwtAccessTokenValidator(options);

        var token = CreateJwt(new
        {
            sub = "user123",
            aud = new[] { "https://api1.example.com", "https://api2.example.com" },
            exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        });

        var result = await validator.ValidateAsync(token, null);

        Assert.True(result.IsValid);
        Assert.Equal(2, result.Audiences.Count);
        Assert.Contains("https://api1.example.com", result.Audiences);
        Assert.Contains("https://api2.example.com", result.Audiences);
    }

    [Fact]
    public async Task ValidateAsync_FailsWhenTokenExpired()
    {
        var options = new TokenValidationOptions { ClockSkew = TimeSpan.Zero };
        var validator = new JwtAccessTokenValidator(options);

        var token = CreateJwt(new
        {
            sub = "user123",
            exp = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds()
        });

        var result = await validator.ValidateAsync(token, null);

        Assert.False(result.IsValid);
        Assert.Equal("invalid_token", result.ErrorCode);
        Assert.Contains("expired", result.ErrorDescription, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_SucceedsWithClockSkew()
    {
        var options = new TokenValidationOptions { ClockSkew = TimeSpan.FromMinutes(10) };
        var validator = new JwtAccessTokenValidator(options);

        // Token expired 5 minutes ago, but clock skew is 10 minutes
        var token = CreateJwt(new
        {
            sub = "user123",
            exp = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeSeconds()
        });

        var result = await validator.ValidateAsync(token, null);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_ValidatesAudienceWhenEnabled()
    {
        var options = new TokenValidationOptions { ValidateAudience = true };
        var validator = new JwtAccessTokenValidator(options);

        var token = CreateJwt(new
        {
            sub = "user123",
            aud = "https://api.example.com",
            exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        });

        var result = await validator.ValidateAsync(token, "https://other.example.com");

        Assert.False(result.IsValid);
        Assert.Equal("invalid_token", result.ErrorCode);
        Assert.Contains("audience", result.ErrorDescription, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_PassesWhenAudienceMatches()
    {
        var options = new TokenValidationOptions { ValidateAudience = true };
        var validator = new JwtAccessTokenValidator(options);

        var token = CreateJwt(new
        {
            sub = "user123",
            aud = "https://api.example.com",
            exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        });

        var result = await validator.ValidateAsync(token, "https://api.example.com");

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_PassesWhenAudienceMatchesInArray()
    {
        var options = new TokenValidationOptions { ValidateAudience = true };
        var validator = new JwtAccessTokenValidator(options);

        var token = CreateJwt(new
        {
            sub = "user123",
            aud = new[] { "https://api1.example.com", "https://api2.example.com" },
            exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        });

        var result = await validator.ValidateAsync(token, "https://api2.example.com");

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_SkipsAudienceValidationWhenNull()
    {
        var options = new TokenValidationOptions { ValidateAudience = true };
        var validator = new JwtAccessTokenValidator(options);

        var token = CreateJwt(new
        {
            sub = "user123",
            aud = "https://api.example.com",
            exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        });

        // When expectedAudience is null, validation is skipped
        var result = await validator.ValidateAsync(token, null);

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_ExtractsSubjectAndIssuer()
    {
        var options = new TokenValidationOptions();
        var validator = new JwtAccessTokenValidator(options);

        var token = CreateJwt(new
        {
            sub = "user123",
            iss = "https://auth.example.com",
            exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        });

        var result = await validator.ValidateAsync(token, null);

        Assert.True(result.IsValid);
        Assert.Equal("user123", result.Subject);
        Assert.Equal("https://auth.example.com", result.Issuer);
    }

    [Fact]
    public async Task ValidateAsync_FailsForInvalidJwtFormat()
    {
        var options = new TokenValidationOptions();
        var validator = new JwtAccessTokenValidator(options);

        var result = await validator.ValidateAsync("not-a-jwt", null);

        Assert.False(result.IsValid);
        Assert.Equal("invalid_token", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateAsync_FailsForInvalidBase64()
    {
        var options = new TokenValidationOptions();
        var validator = new JwtAccessTokenValidator(options);

        var result = await validator.ValidateAsync("header.!!!invalid-base64!!!.signature", null);

        Assert.False(result.IsValid);
        Assert.Equal("invalid_token", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateAsync_FailsForInvalidJson()
    {
        var options = new TokenValidationOptions();
        var validator = new JwtAccessTokenValidator(options);

        var headerB64 = Base64UrlEncode(Encoding.UTF8.GetBytes("{}"));
        var payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes("not-json"));
        var token = $"{headerB64}.{payloadB64}.signature";

        var result = await validator.ValidateAsync(token, null);

        Assert.False(result.IsValid);
        Assert.Equal("invalid_token", result.ErrorCode);
    }

    [Fact]
    public async Task ValidateAsync_ExtractsExpirationTime()
    {
        var options = new TokenValidationOptions();
        var validator = new JwtAccessTokenValidator(options);

        var expTime = DateTimeOffset.UtcNow.AddHours(2);
        var token = CreateJwt(new
        {
            sub = "user123",
            exp = expTime.ToUnixTimeSeconds()
        });

        var result = await validator.ValidateAsync(token, null);

        Assert.True(result.IsValid);
        Assert.NotNull(result.ExpiresAt);
        // Allow 1 second tolerance for test execution time
        Assert.True(Math.Abs((result.ExpiresAt.Value - expTime).TotalSeconds) < 1);
    }

    [Fact]
    public async Task ValidateAsync_UsesConfiguredScopeClaimName()
    {
        var options = new TokenValidationOptions { ScopeClaimName = "permissions" };
        var validator = new JwtAccessTokenValidator(options);

        var token = CreateJwt(new
        {
            sub = "user123",
            permissions = "read write",
            exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds()
        });

        var result = await validator.ValidateAsync(token, null);

        Assert.True(result.IsValid);
        Assert.Equal(2, result.Scopes.Count);
        Assert.Contains("read", result.Scopes);
        Assert.Contains("write", result.Scopes);
    }
}
