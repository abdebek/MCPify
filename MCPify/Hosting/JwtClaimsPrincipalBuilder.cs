using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace MCPify.Hosting;

/// <summary>
/// Builds a <see cref="ClaimsPrincipal"/> from a JWT access token for scope enforcement
/// in non-HTTP contexts (e.g. stdio transport where auth middleware does not run).
/// This performs signature-less payload extraction — scopes are read for authorization
/// decisions only, not for authentication. Authentication is the MCP handler's job.
/// </summary>
internal static class JwtClaimsPrincipalBuilder
{
    public static ClaimsPrincipal? BuildFromJwt(string jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt))
        {
            return null;
        }

        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2)
            {
                return null;
            }

            var payload = DecodeBase64Url(parts[1]);
            using var doc = JsonDocument.Parse(payload);

            var claims = new List<Claim>();
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                var value = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Number => property.Value.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => null,
                    _ => property.Value.GetRawText()
                };

                if (value != null)
                {
                    // For array-valued scope claims, split into individual claims
                    if (property.NameEquals("scope") || property.NameEquals("scp"))
                    {
                        foreach (var scope in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        {
                            claims.Add(new Claim("scope", scope));
                        }
                    }
                    else
                    {
                        claims.Add(new Claim(property.Name, value));
                    }
                }
            }

            var identity = new ClaimsIdentity(claims, "MCPifyJwt");
            return new ClaimsPrincipal(identity);
        }
        catch
        {
            return null;
        }
    }

    private static string DecodeBase64Url(string input)
    {
        var output = input.Replace('-', '+').Replace('_', '/');
        switch (output.Length % 4)
        {
            case 0: break;
            case 2: output += "=="; break;
            case 3: output += "="; break;
            default: throw new FormatException("Illegal base64url string!");
        }
        return Encoding.UTF8.GetString(Convert.FromBase64String(output));
    }
}