using MCPify.Core.Auth;

namespace MCPify.Tests.Unit;

public class ScopeRequirementTests
{
    #region ScopeRequirement Pattern Matching Tests

    [Theory]
    [InlineData("admin_users", "admin_users", true)]
    [InlineData("admin_users", "admin_roles", false)]
    [InlineData("admin_*", "admin_users", true)]
    [InlineData("admin_*", "admin_roles", true)]
    [InlineData("admin_*", "user_admin", false)]
    [InlineData("*_admin", "user_admin", true)]
    [InlineData("*_admin", "admin_users", false)]
    [InlineData("api_?et_users", "api_get_users", true)]
    [InlineData("api_?et_users", "api_set_users", true)]
    [InlineData("api_?et_users", "api_delete_users", false)]
    [InlineData("*", "anything", true)]
    [InlineData("tool_*_admin", "tool_user_admin", true)]
    [InlineData("tool_*_admin", "tool_role_admin", true)]
    [InlineData("tool_*_admin", "tool_admin", false)]
    public void Matches_WorksWithPatterns(string pattern, string toolName, bool expected)
    {
        var requirement = new ScopeRequirement { Pattern = pattern };
        Assert.Equal(expected, requirement.Matches(toolName));
    }

    [Fact]
    public void Matches_IsCaseInsensitive()
    {
        var requirement = new ScopeRequirement { Pattern = "Admin_*" };

        Assert.True(requirement.Matches("admin_users"));
        Assert.True(requirement.Matches("ADMIN_ROLES"));
        Assert.True(requirement.Matches("Admin_Tools"));
    }

    #endregion

    #region ScopeRequirement Scope Validation Tests

    [Fact]
    public void IsSatisfiedBy_RequiresAllRequiredScopes()
    {
        var requirement = new ScopeRequirement
        {
            Pattern = "admin_*",
            RequiredScopes = new List<string> { "admin", "write" }
        };

        Assert.True(requirement.IsSatisfiedBy(new[] { "admin", "write", "read" }));
        Assert.False(requirement.IsSatisfiedBy(new[] { "admin", "read" }));
        Assert.False(requirement.IsSatisfiedBy(new[] { "write", "read" }));
        Assert.False(requirement.IsSatisfiedBy(new[] { "read" }));
    }

    [Fact]
    public void IsSatisfiedBy_RequiresAnyOfScopes()
    {
        var requirement = new ScopeRequirement
        {
            Pattern = "api_*",
            AnyOfScopes = new List<string> { "read", "write" }
        };

        Assert.True(requirement.IsSatisfiedBy(new[] { "read" }));
        Assert.True(requirement.IsSatisfiedBy(new[] { "write" }));
        Assert.True(requirement.IsSatisfiedBy(new[] { "read", "write" }));
        Assert.False(requirement.IsSatisfiedBy(new[] { "admin" }));
        Assert.False(requirement.IsSatisfiedBy(Array.Empty<string>()));
    }

    [Fact]
    public void IsSatisfiedBy_CombinesRequiredAndAnyOf()
    {
        var requirement = new ScopeRequirement
        {
            Pattern = "admin_*",
            RequiredScopes = new List<string> { "admin" },
            AnyOfScopes = new List<string> { "read", "write" }
        };

        Assert.True(requirement.IsSatisfiedBy(new[] { "admin", "read" }));
        Assert.True(requirement.IsSatisfiedBy(new[] { "admin", "write" }));
        Assert.True(requirement.IsSatisfiedBy(new[] { "admin", "read", "write" }));
        Assert.False(requirement.IsSatisfiedBy(new[] { "admin" })); // Missing any of read/write
        Assert.False(requirement.IsSatisfiedBy(new[] { "read" })); // Missing required admin
        Assert.False(requirement.IsSatisfiedBy(new[] { "read", "write" })); // Missing required admin
    }

    [Fact]
    public void IsSatisfiedBy_IsCaseInsensitive()
    {
        var requirement = new ScopeRequirement
        {
            Pattern = "api_*",
            RequiredScopes = new List<string> { "Admin" },
            AnyOfScopes = new List<string> { "Read" }
        };

        Assert.True(requirement.IsSatisfiedBy(new[] { "ADMIN", "read" }));
        Assert.True(requirement.IsSatisfiedBy(new[] { "admin", "READ" }));
    }

    [Fact]
    public void IsSatisfiedBy_PassesWithEmptyRequirements()
    {
        var requirement = new ScopeRequirement { Pattern = "api_*" };

        Assert.True(requirement.IsSatisfiedBy(Array.Empty<string>()));
        Assert.True(requirement.IsSatisfiedBy(new[] { "any", "scope" }));
    }

    [Fact]
    public void GetAllRequiredScopes_ReturnsAllScopes()
    {
        var requirement = new ScopeRequirement
        {
            Pattern = "api_*",
            RequiredScopes = new List<string> { "admin", "write" },
            AnyOfScopes = new List<string> { "read", "execute" }
        };

        var allScopes = requirement.GetAllRequiredScopes().ToList();

        Assert.Equal(4, allScopes.Count);
        Assert.Contains("admin", allScopes);
        Assert.Contains("write", allScopes);
        Assert.Contains("read", allScopes);
        Assert.Contains("execute", allScopes);
    }

    #endregion

    #region ScopeRequirementStore Tests

    [Fact]
    public void GetRequirementsForTool_ReturnsMatchingRequirements()
    {
        var options = new TokenValidationOptions();
        var requirements = new List<ScopeRequirement>
        {
            new() { Pattern = "admin_*", RequiredScopes = new List<string> { "admin" } },
            new() { Pattern = "api_*", RequiredScopes = new List<string> { "api" } },
            new() { Pattern = "*_users", RequiredScopes = new List<string> { "users" } }
        };
        var store = new ScopeRequirementStore(requirements, options);

        var adminUsersReqs = store.GetRequirementsForTool("admin_users").ToList();
        Assert.Equal(2, adminUsersReqs.Count); // Matches "admin_*" and "*_users"

        var apiGetReqs = store.GetRequirementsForTool("api_get").ToList();
        Assert.Single(apiGetReqs); // Matches only "api_*"

        var otherReqs = store.GetRequirementsForTool("other_tool").ToList();
        Assert.Empty(otherReqs);
    }

    [Fact]
    public void ValidateScopesForTool_ChecksDefaultScopes()
    {
        var options = new TokenValidationOptions
        {
            DefaultRequiredScopes = new List<string> { "mcp.access" }
        };
        var store = new ScopeRequirementStore(new List<ScopeRequirement>(), options);

        var result = store.ValidateScopesForTool("any_tool", new[] { "mcp.access" });
        Assert.True(result.IsValid);

        var failResult = store.ValidateScopesForTool("any_tool", new[] { "other" });
        Assert.False(failResult.IsValid);
        Assert.Contains("mcp.access", failResult.MissingScopes);
    }

    [Fact]
    public void ValidateScopesForTool_ChecksToolSpecificScopes()
    {
        var options = new TokenValidationOptions();
        var requirements = new List<ScopeRequirement>
        {
            new() { Pattern = "admin_*", RequiredScopes = new List<string> { "admin" } }
        };
        var store = new ScopeRequirementStore(requirements, options);

        var result = store.ValidateScopesForTool("admin_users", new[] { "admin" });
        Assert.True(result.IsValid);

        var failResult = store.ValidateScopesForTool("admin_users", new[] { "user" });
        Assert.False(failResult.IsValid);
        Assert.Contains("admin", failResult.MissingScopes);
    }

    [Fact]
    public void ValidateScopesForTool_CombinesDefaultAndToolSpecificScopes()
    {
        var options = new TokenValidationOptions
        {
            DefaultRequiredScopes = new List<string> { "mcp.access" }
        };
        var requirements = new List<ScopeRequirement>
        {
            new() { Pattern = "admin_*", RequiredScopes = new List<string> { "admin" } }
        };
        var store = new ScopeRequirementStore(requirements, options);

        var result = store.ValidateScopesForTool("admin_users", new[] { "mcp.access", "admin" });
        Assert.True(result.IsValid);

        var failResult = store.ValidateScopesForTool("admin_users", new[] { "mcp.access" });
        Assert.False(failResult.IsValid);
        Assert.Contains("admin", failResult.MissingScopes);

        var failResult2 = store.ValidateScopesForTool("admin_users", new[] { "admin" });
        Assert.False(failResult2.IsValid);
        Assert.Contains("mcp.access", failResult2.MissingScopes);
    }

    [Fact]
    public void GetRequiredScopesForTool_ReturnsAllRequiredScopes()
    {
        var options = new TokenValidationOptions
        {
            DefaultRequiredScopes = new List<string> { "mcp.access" }
        };
        var requirements = new List<ScopeRequirement>
        {
            new() { Pattern = "admin_*", RequiredScopes = new List<string> { "admin" }, AnyOfScopes = new List<string> { "read", "write" } }
        };
        var store = new ScopeRequirementStore(requirements, options);

        var scopes = store.GetRequiredScopesForTool("admin_users").ToList();

        Assert.Contains("mcp.access", scopes);
        Assert.Contains("admin", scopes);
        Assert.Contains("read", scopes);
        Assert.Contains("write", scopes);
    }

    [Fact]
    public void ValidateScopesForTool_HandlesMultipleMatchingRequirements()
    {
        var options = new TokenValidationOptions();
        var requirements = new List<ScopeRequirement>
        {
            new() { Pattern = "admin_*", RequiredScopes = new List<string> { "admin" } },
            new() { Pattern = "*_users", RequiredScopes = new List<string> { "users.manage" } }
        };
        var store = new ScopeRequirementStore(requirements, options);

        // admin_users matches both patterns, so both scopes are required
        var result = store.ValidateScopesForTool("admin_users", new[] { "admin", "users.manage" });
        Assert.True(result.IsValid);

        var failResult = store.ValidateScopesForTool("admin_users", new[] { "admin" });
        Assert.False(failResult.IsValid);
        Assert.Contains("users.manage", failResult.MissingScopes);
    }

    [Fact]
    public void ValidateScopesForTool_UsesOAuthConfiguredScopes_WhenEnabled()
    {
        var oauthStore = new OAuthConfigurationStore();
        oauthStore.AddConfiguration(new OAuth2Configuration
        {
            AuthorizationUrl = "https://auth",
            Scopes = new Dictionary<string, string>
            {
                { "read", "Read access" },
                { "write", "Write access" }
            }
        });

        var options = new TokenValidationOptions
        {
            RequireOAuthConfiguredScopes = true
        };
        var store = new ScopeRequirementStore(new List<ScopeRequirement>(), options, oauthStore);

        // Token with all OAuth-configured scopes should pass
        var result = store.ValidateScopesForTool("any_tool", new[] { "read", "write" });
        Assert.True(result.IsValid);

        // Token missing one OAuth-configured scope should fail
        var failResult = store.ValidateScopesForTool("any_tool", new[] { "read" });
        Assert.False(failResult.IsValid);
        Assert.Contains("write", failResult.MissingScopes);
    }

    [Fact]
    public void ValidateScopesForTool_IgnoresOAuthScopes_WhenDisabled()
    {
        var oauthStore = new OAuthConfigurationStore();
        oauthStore.AddConfiguration(new OAuth2Configuration
        {
            AuthorizationUrl = "https://auth",
            Scopes = new Dictionary<string, string>
            {
                { "read", "Read access" },
                { "write", "Write access" }
            }
        });

        var options = new TokenValidationOptions
        {
            RequireOAuthConfiguredScopes = false // disabled by default
        };
        var store = new ScopeRequirementStore(new List<ScopeRequirement>(), options, oauthStore);

        // Token with no scopes should pass when OAuth scope checking is disabled
        var result = store.ValidateScopesForTool("any_tool", Array.Empty<string>());
        Assert.True(result.IsValid);
    }

    [Fact]
    public void GetRequiredScopesForTool_IncludesOAuthScopes_WhenEnabled()
    {
        var oauthStore = new OAuthConfigurationStore();
        oauthStore.AddConfiguration(new OAuth2Configuration
        {
            AuthorizationUrl = "https://auth",
            Scopes = new Dictionary<string, string>
            {
                { "api.read", "Read API" },
                { "api.write", "Write API" }
            }
        });

        var options = new TokenValidationOptions
        {
            RequireOAuthConfiguredScopes = true,
            DefaultRequiredScopes = new List<string> { "mcp.access" }
        };
        var store = new ScopeRequirementStore(new List<ScopeRequirement>(), options, oauthStore);

        var scopes = store.GetRequiredScopesForTool("any_tool").ToList();

        Assert.Contains("mcp.access", scopes);
        Assert.Contains("api.read", scopes);
        Assert.Contains("api.write", scopes);
    }

    #endregion
}
