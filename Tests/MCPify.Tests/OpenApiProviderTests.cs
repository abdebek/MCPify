using MCPify.OpenApi;
using Microsoft.OpenApi.Models;

namespace MCPify.Tests;

public class OpenApiProviderTests
{
    [Fact]
    public async Task LoadAsync_LoadsLocalOpenApiFile_AndExtractsOperations()
    {
        var specPath = Path.Combine(AppContext.BaseDirectory, "TestData", "bank-openapi.json");
        Assert.True(File.Exists(specPath), $"Spec file not found at {specPath}");

        var provider = new OpenApiV3Provider(TimeSpan.FromSeconds(10));

        var document = await provider.LoadAsync(specPath);

        var operations = provider.GetOperations(document).ToList();

        var expectedOperations = new[]
        {
            "GetAllBanks",
            "CreateBank",
            "GetBank",
            "UpdateBank",
            "DeleteBank",
            "GetBankTeller",
            "GetBankTellerReports"
        };

        Assert.Equal(expectedOperations.Length, operations.Count);
        foreach (var opId in expectedOperations)
        {
            var op = operations.Single(o => o.Name == opId);
            Assert.False(string.IsNullOrWhiteSpace(op.Route));
            Assert.Contains(op.Method, Enum.GetValues<OperationType>());
        }

        var getBank = operations.Single(o => o.Name == "GetBank");
        Assert.Equal("/banks/{id}", getBank.Route);
        Assert.Equal(OperationType.Get, getBank.Method);
    }
}
