namespace MCPify.Core.Auth;

/// <summary>
/// Registry for scope requirements with pattern matching.
/// Determines required scopes for tools based on configured patterns.
/// </summary>
public class ScopeRequirementStore
{
    private readonly List<ScopeRequirement> _requirements;
    private readonly TokenValidationOptions _options;
    private readonly OAuthConfigurationStore? _oauthStore;

    public ScopeRequirementStore(IEnumerable<ScopeRequirement> requirements, TokenValidationOptions options, OAuthConfigurationStore? oauthStore = null)
    {
        _requirements = requirements.ToList();
        _options = options;
        _oauthStore = oauthStore;
    }

    /// <summary>
    /// Gets all scope requirements that apply to the given tool name.
    /// </summary>
    /// <param name="toolName">The name of the tool.</param>
    /// <returns>All matching scope requirements.</returns>
    public IEnumerable<ScopeRequirement> GetRequirementsForTool(string toolName)
    {
        return _requirements.Where(r => r.Matches(toolName));
    }

    /// <summary>
    /// Validates that the provided scopes satisfy all requirements for the given tool.
    /// </summary>
    /// <param name="toolName">The name of the tool.</param>
    /// <param name="tokenScopes">Scopes from the access token.</param>
    /// <returns>A validation result with missing scopes if validation fails.</returns>
    public ScopeValidationResult ValidateScopesForTool(string toolName, IEnumerable<string> tokenScopes)
    {
        var scopeList = tokenScopes.ToList();
        var scopeSet = new HashSet<string>(scopeList, StringComparer.OrdinalIgnoreCase);
        var missingScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Check default required scopes first
        foreach (var defaultScope in _options.DefaultRequiredScopes)
        {
            if (!scopeSet.Contains(defaultScope))
            {
                missingScopes.Add(defaultScope);
            }
        }

        // Check OAuth-configured scopes if enabled
        if (_options.RequireOAuthConfiguredScopes && _oauthStore != null)
        {
            var oauthScopes = _oauthStore.GetConfigurations()
                .SelectMany(c => c.Scopes.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var oauthScope in oauthScopes)
            {
                if (!scopeSet.Contains(oauthScope))
                {
                    missingScopes.Add(oauthScope);
                }
            }
        }

        // Check tool-specific requirements
        var matchingRequirements = GetRequirementsForTool(toolName).ToList();
        foreach (var requirement in matchingRequirements)
        {
            if (!requirement.IsSatisfiedBy(scopeList))
            {
                // Add all scopes from this requirement to missing list
                foreach (var scope in requirement.GetAllRequiredScopes())
                {
                    if (!scopeSet.Contains(scope))
                    {
                        missingScopes.Add(scope);
                    }
                }
            }
        }

        if (missingScopes.Count > 0)
        {
            return ScopeValidationResult.Failure(missingScopes.ToList());
        }

        return ScopeValidationResult.Success();
    }

    /// <summary>
    /// Gets all required scopes for a tool (for WWW-Authenticate header).
    /// </summary>
    public IEnumerable<string> GetRequiredScopesForTool(string toolName)
    {
        var scopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Add default scopes
        foreach (var scope in _options.DefaultRequiredScopes)
        {
            scopes.Add(scope);
        }

        // Add OAuth-configured scopes if enabled
        if (_options.RequireOAuthConfiguredScopes && _oauthStore != null)
        {
            foreach (var config in _oauthStore.GetConfigurations())
            {
                foreach (var scope in config.Scopes.Keys)
                {
                    scopes.Add(scope);
                }
            }
        }

        // Add tool-specific scopes
        foreach (var requirement in GetRequirementsForTool(toolName))
        {
            foreach (var scope in requirement.GetAllRequiredScopes())
            {
                scopes.Add(scope);
            }
        }

        return scopes;
    }
}

/// <summary>
/// Result of scope validation.
/// </summary>
public class ScopeValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<string> MissingScopes { get; init; } = Array.Empty<string>();

    public static ScopeValidationResult Success() => new() { IsValid = true };

    public static ScopeValidationResult Failure(IReadOnlyList<string> missingScopes) => new()
    {
        IsValid = false,
        MissingScopes = missingScopes
    };
}
