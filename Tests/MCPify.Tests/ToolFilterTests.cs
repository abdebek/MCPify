using MCPify.Core;
using MCPify.Hosting;
using MCPify.OpenApi;
using MCPify.Schema;
using MCPify.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using ModelContextProtocol.Server;
using Moq;
using Xunit;

namespace MCPify.Tests;

public class ToolFilterTests
{
    private static OpenApiOperationDescriptor Op(string name, string route, OperationType method, params string[] tags)
    {
        var op = new OpenApiOperation
        {
            OperationId = name,
            Tags = tags.Select(t => new OpenApiTag { Name = t }).ToList(),
        };
        return new OpenApiOperationDescriptor(name, route, method, op);
    }

    [Fact]
    public void AllowPaths_FiltersByPrefix()
    {
        var filter = new ToolFilter { AllowPaths = { "/api/v1" } };
        Assert.True(filter.Matches(Op("get_users", "/api/v1/users", OperationType.Get)));
        Assert.False(filter.Matches(Op("get_legacy", "/api/v0/users", OperationType.Get)));
    }

    [Fact]
    public void DenyPaths_ExcludesByPrefix()
    {
        var filter = new ToolFilter { DenyPaths = { "/internal" } };
        Assert.False(filter.Matches(Op("health", "/internal/health", OperationType.Get)));
        Assert.True(filter.Matches(Op("users", "/api/users", OperationType.Get)));
    }

    [Fact]
    public void AllowMethods_FiltersByMethod()
    {
        var filter = new ToolFilter { AllowMethods = { "GET", "POST" } };
        Assert.True(filter.Matches(Op("get", "/r", OperationType.Get)));
        Assert.True(filter.Matches(Op("post", "/r", OperationType.Post)));
        Assert.False(filter.Matches(Op("delete", "/r", OperationType.Delete)));
    }

    [Fact]
    public void DenyMethods_ExcludesByMethod()
    {
        var filter = new ToolFilter { DenyMethods = { "DELETE" } };
        Assert.False(filter.Matches(Op("delete", "/r", OperationType.Delete)));
        Assert.True(filter.Matches(Op("get", "/r", OperationType.Get)));
    }

    [Fact]
    public void AllowTags_FiltersByTag()
    {
        var filter = new ToolFilter { AllowTags = { "pets" } };
        Assert.True(filter.Matches(Op("listPets", "/pets", OperationType.Get, "pets")));
        Assert.False(filter.Matches(Op("listUsers", "/users", OperationType.Get, "users")));
    }

    [Fact]
    public void DenyTags_ExcludesByTag()
    {
        var filter = new ToolFilter { DenyTags = { "admin" } };
        Assert.False(filter.Matches(Op("adminOp", "/admin", OperationType.Post, "admin")));
        Assert.True(filter.Matches(Op("userOp", "/users", OperationType.Get, "users")));
    }

    [Fact]
    public void AllowOperationIds_FiltersByPrefix()
    {
        var filter = new ToolFilter { AllowOperationIds = { "get_" } };
        Assert.True(filter.Matches(Op("get_users", "/users", OperationType.Get)));
        Assert.False(filter.Matches(Op("create_user", "/users", OperationType.Post)));
    }

    [Fact]
    public void ExcludeDeprecated_FiltersDeprecatedOps()
    {
        var filter = new ToolFilter { ExcludeDeprecated = true };
        Assert.False(filter.Matches(new OpenApiOperationDescriptor(
            "old", "/old", OperationType.Get, new OpenApiOperation { Deprecated = true })));
        Assert.True(filter.Matches(Op("new", "/new", OperationType.Get)));
    }

    [Fact]
    public void CombinedAllowAndDeny_DenyTakesPrecedence()
    {
        var filter = new ToolFilter
        {
            AllowPaths = { "/api" },
            DenyPaths = { "/api/internal" },
        };
        Assert.True(filter.Matches(Op("users", "/api/users", OperationType.Get)));
        Assert.False(filter.Matches(Op("internal", "/api/internal/secret", OperationType.Get)));
    }

    [Fact]
    public void EmptyFilter_AllowsAll()
    {
        var filter = new ToolFilter();
        Assert.True(filter.Matches(Op("any", "/any", OperationType.Get, "any")));
    }
}