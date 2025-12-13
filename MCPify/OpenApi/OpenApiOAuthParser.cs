using Microsoft.OpenApi.Models;
using MCPify.Core.Auth;

namespace MCPify.OpenApi;

public class OpenApiOAuthParser
{
    public OAuth2Configuration? Parse(OpenApiDocument document)
    {
        if (document.Components?.SecuritySchemes == null)
        {
            return null;
        }

        foreach (var scheme in document.Components.SecuritySchemes.Values)
        {
            if (scheme.Type != SecuritySchemeType.OAuth2)
            {
                continue;
            }

            // We prioritize Authorization Code flow
            if (scheme.Flows?.AuthorizationCode != null)
            {
                return CreateConfiguration(scheme.Flows.AuthorizationCode, "authorization_code");
            }
            
            // Fallback to other flows if needed, but the requirement specifically mentions Authorization Code.
            // For now, let's stick to the requirement: "Prioritize OAuth2 authorization code flow".
            // If we want to support others later (like Implicit or ClientCredentials), we can add them here.
        }

        return null;
    }

    private static OAuth2Configuration CreateConfiguration(OpenApiOAuthFlow flow, string flowType)
    {
        return new OAuth2Configuration
        {
            AuthorizationUrl = flow.AuthorizationUrl?.ToString() ?? string.Empty,
            TokenUrl = flow.TokenUrl?.ToString() ?? string.Empty,
            RefreshUrl = flow.RefreshUrl?.ToString(),
            Scopes = flow.Scopes?.ToDictionary(k => k.Key, k => k.Value) ?? new Dictionary<string, string>(),
            FlowType = flowType
        };
    }
}
