using System.Text.RegularExpressions;

namespace MCPify.Core.Auth;

/// <summary>
/// Defines scope requirements for a tool or endpoint.
/// </summary>
public class ScopeRequirement
{
    /// <summary>
    /// Tool name pattern to match. Supports wildcards:
    /// - '*' matches any sequence of characters
    /// - '?' matches any single character
    /// Examples: "admin_*", "api_get_*", "tool_name"
    /// </summary>
    public required string Pattern { get; init; }

    /// <summary>
    /// Scopes that must ALL be present in the token.
    /// If empty, only <see cref="AnyOfScopes"/> is checked.
    /// </summary>
    public List<string> RequiredScopes { get; init; } = new();

    /// <summary>
    /// At least ONE of these scopes must be present in the token.
    /// If empty, only <see cref="RequiredScopes"/> is checked.
    /// </summary>
    public List<string> AnyOfScopes { get; init; } = new();

    private Regex? _compiledPattern;

    /// <summary>
    /// Checks if this requirement applies to the given tool name.
    /// </summary>
    public bool Matches(string toolName)
    {
        _compiledPattern ??= CompilePattern(Pattern);
        return _compiledPattern.IsMatch(toolName);
    }

    /// <summary>
    /// Validates that the provided scopes satisfy this requirement.
    /// </summary>
    /// <param name="tokenScopes">Scopes from the access token.</param>
    /// <returns>True if the scopes satisfy the requirement, false otherwise.</returns>
    public bool IsSatisfiedBy(IEnumerable<string> tokenScopes)
    {
        var scopeSet = new HashSet<string>(tokenScopes, StringComparer.OrdinalIgnoreCase);

        // Check RequiredScopes - all must be present
        if (RequiredScopes.Count > 0)
        {
            foreach (var required in RequiredScopes)
            {
                if (!scopeSet.Contains(required))
                {
                    return false;
                }
            }
        }

        // Check AnyOfScopes - at least one must be present
        if (AnyOfScopes.Count > 0)
        {
            var hasAny = AnyOfScopes.Any(s => scopeSet.Contains(s));
            if (!hasAny)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Gets all scopes that are required by this requirement (for error messages).
    /// </summary>
    public IEnumerable<string> GetAllRequiredScopes()
    {
        foreach (var scope in RequiredScopes)
        {
            yield return scope;
        }
        foreach (var scope in AnyOfScopes)
        {
            yield return scope;
        }
    }

    private static Regex CompilePattern(string pattern)
    {
        // Escape regex special characters except * and ?
        var escaped = Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".");

        return new Regex($"^{escaped}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}
