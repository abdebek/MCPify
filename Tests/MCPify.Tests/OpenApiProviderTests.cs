using MCPify.OpenApi;
using Microsoft.OpenApi.Models;

namespace MCPify.Tests;

public class OpenApiProviderTests
{
    [Fact]
    public async Task LoadAsync_LoadsOpenApi31_AndDowngradesSuccessfully()
    {
        var specPath = Path.Combine(AppContext.BaseDirectory, "TestData", "sample-openapi-3.1.json");

        Assert.True(File.Exists(specPath), $"Spec file not found at {specPath}");

        var provider = new OpenApiV3Provider(TimeSpan.FromSeconds(10));
        var document = await provider.LoadAsync(specPath);

        Assert.NotNull(document);

        var operations = provider.GetOperations(document).ToList();
        Assert.NotEmpty(operations);

        if (document.Components.Schemas.ContainsKey("EdgeCaseModel"))
        {
            var schema = document.Components.Schemas["EdgeCaseModel"];

            var propBase64 = schema.Properties["prop_base64"];
            Assert.Equal("string", propBase64.Type);
            Assert.Equal("byte", propBase64.Format);

            var propNullable = schema.Properties["prop_nullable_array"];
            Assert.Equal("string", propNullable.Type);
            Assert.True(propNullable.Nullable);

            var propExclusive = schema.Properties["prop_exclusive_min"];
            Assert.Equal(10, propExclusive.Minimum);
            Assert.True(propExclusive.ExclusiveMinimum);

            var op = operations.Single(o => o.Name == "GetTest");
            Assert.Equal("/test", op.Route);
            Assert.Equal(OperationType.Get, op.Method);
        }
    }
}