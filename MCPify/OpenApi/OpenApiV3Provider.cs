using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using MCPify.Core;

namespace MCPify.OpenApi;

public class OpenApiV3Provider : IOpenApiProvider
{
    public async Task<OpenApiDocument> LoadAsync(string source)
    {
        using var httpClient = new HttpClient();

        Stream stream;
        if (Uri.IsWellFormedUriString(source, UriKind.Absolute))
        {
            stream = await httpClient.GetStreamAsync(source);
        }
        else
        {
            stream = File.OpenRead(source);
        }

        var reader = new OpenApiStreamReader();
        var document = reader.Read(stream, out var diagnostic);

        if (diagnostic.Errors.Count > 0)
        {
            var errors = string.Join(", ", diagnostic.Errors.Select(e => e.Message));
            throw new InvalidOperationException($"OpenAPI document has errors: {errors}");
        }

        return document;
    }

    public IEnumerable<OpenApiOperationDescriptor> GetOperations(OpenApiDocument doc)
    {
        foreach (var path in doc.Paths)
        {
            foreach (var operation in path.Value.Operations)
            {
                var operationId = operation.Value.OperationId
                    ?? $"{operation.Key}_{path.Key.Replace("/", "_").Trim('_')}";

                yield return new OpenApiOperationDescriptor(
                    Name: operationId,
                    Route: path.Key,
                    Method: operation.Key,
                    Operation: operation.Value
                );
            }
        }
    }
}
