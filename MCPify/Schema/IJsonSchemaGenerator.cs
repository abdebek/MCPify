using Microsoft.OpenApi.Models;

namespace MCPify.Schema;

public interface IJsonSchemaGenerator
{
    object GenerateInputSchema(OpenApiOperation operation);
    object? GenerateOutputSchema(OpenApiOperation operation);
}
