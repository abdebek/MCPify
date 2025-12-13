using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Exceptions;
using Microsoft.OpenApi.Readers;
using Microsoft.OpenApi.Readers.Exceptions;
using MCPify.Core;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;

namespace MCPify.OpenApi;

public class OpenApiV3Provider : IOpenApiProvider
{
    private readonly TimeSpan _timeout;

    public OpenApiV3Provider(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
    }

    public async Task<OpenApiDocument> LoadAsync(string source)
    {
        var content = await ReadContentAsync(source);
        return ParseWithFallback(content);
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

    private async Task<string> ReadContentAsync(string source)
    {
        if (Uri.IsWellFormedUriString(source, UriKind.Absolute))
        {
            using var httpClient = new HttpClient { Timeout = _timeout };
            return await httpClient.GetStringAsync(source);
        }

        return await File.ReadAllTextAsync(source);
    }

    private OpenApiDocument ParseWithFallback(string content)
    {
        try
        {
            return Parse(content);
        }
        catch (OpenApiUnsupportedSpecVersionException) when (content.Contains("\"openapi\": \"3.1"))
        {
            var downgraded = DowngradeOpenApi31To30(content);
            return Parse(downgraded);
        }
        catch (OpenApiException)
        {
            // If the reader fails (e.g., "Cannot create scalar value") but it looks like 3.1, attempt downgrade.
            if (content.Contains("\"openapi\": \"3.1"))
            {
                var downgraded = DowngradeOpenApi31To30(content);
                return Parse(downgraded);
            }
            throw;
        }
    }

    private static string DowngradeOpenApi31To30(string content)
    {
        try
        {
            var node = JsonNode.Parse(content);
            if (node is JsonObject obj)
            {
                // 1. Fix version
                obj["openapi"] = "3.0.3";
                obj.AsObject().Remove("jsonSchemaDialect");

                // 2. Fix nullable arrays (["null", "string"] -> nullable: true)
                DowngradeNullability(obj);

                return obj.ToJsonString();
            }
        }
        catch
        {
            // Fallback: This regex only fixes the version, not the array types.
            // Only hits if JSON parsing fails completely.
        }

        return Regex.Replace(content, "\"openapi\"\\s*:\\s*\"3\\.1\\.[^\"]*\"", "\"openapi\": \"3.0.3\"", RegexOptions.IgnoreCase);
    }

    private static void DowngradeNullability(JsonNode? node)
    {
        if (node == null) return;

        if (node is JsonObject obj)
        {
            // Check if this specific object has the 3.1 type array
            if (obj.TryGetPropertyValue("type", out var typeNode) && typeNode is JsonArray typeArray)
            {
                // Look for ["null", "string"] or ["string", "null"]
                if (typeArray.Count == 2 && typeArray.Any(t => t?.GetValue<string>() == "null"))
                {
                    // Convert to 3.0 format
                    obj["nullable"] = true;
                    var nonNullType = typeArray.First(t => t?.GetValue<string>() != "null");
                    obj["type"] = nonNullType?.GetValue<string>(); // Set single string type
                }
            }

            // Continue recursion for all children
            foreach (var property in obj.ToList()) // ToList avoids modification issues during iteration
            {
                DowngradeNullability(property.Value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var element in array)
            {
                DowngradeNullability(element);
            }
        }
    }

    private static OpenApiDocument Parse(string content)
    {
        var reader = new OpenApiStringReader();
        var document = reader.Read(content, out var diagnostic);

        if (diagnostic.Errors.Count > 0)
        {
            var errors = string.Join(", ", diagnostic.Errors.Select(e => e.Message));
            throw new InvalidOperationException($"OpenAPI document has errors: {errors}");
        }

        return document;
    }
}