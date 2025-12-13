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
        catch (OpenApiUnsupportedSpecVersionException)
        {
            var downgraded = DowngradeOpenApi31To30(content);
            return Parse(downgraded);
        }
        catch (OpenApiException)
        {
            if (content.Contains("\"openapi\": \"3.1") || content.Contains("\"openapi\":\"3.1"))
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
                obj["openapi"] = "3.0.3";
                obj.Remove("jsonSchemaDialect");
                obj.Remove("webhooks");

                DowngradeSchemaFeatures(obj);

                return obj.ToJsonString();
            }
        }
        catch
        {
        }

        return Regex.Replace(content, "\"openapi\"\\s*:\\s*\"3\\.1\\.[^\"]*\"", "\"openapi\": \"3.0.3\"", RegexOptions.IgnoreCase);
    }

    private static void DowngradeSchemaFeatures(JsonNode? node)
    {
        if (node == null) return;

        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("type", out var typeNode) && typeNode is JsonArray typeArray)
            {
                if (typeArray.Any(t => t?.GetValue<string>() == "null"))
                {
                    obj["nullable"] = true;
                    var nonNullType = typeArray.FirstOrDefault(t => t?.GetValue<string>() != "null");
                    if (nonNullType != null)
                    {
                        obj["type"] = nonNullType.GetValue<string>();
                    }
                    else
                    {
                        obj.Remove("type");
                    }
                }
                else if (typeArray.Count > 0)
                {
                    obj["type"] = typeArray.First()?.GetValue<string>();
                }
            }

            if (obj.TryGetPropertyValue("exclusiveMinimum", out var exMinNode) && 
                (exMinNode is JsonValue) && 
                exMinNode.GetValueKind() == System.Text.Json.JsonValueKind.Number)
            {
                var val = exMinNode.GetValue<decimal>();
                obj["minimum"] = val;
                obj["exclusiveMinimum"] = true;
            }

            if (obj.TryGetPropertyValue("exclusiveMaximum", out var exMaxNode) && 
                (exMaxNode is JsonValue) && 
                exMaxNode.GetValueKind() == System.Text.Json.JsonValueKind.Number)
            {
                var val = exMaxNode.GetValue<decimal>();
                obj["maximum"] = val;
                obj["exclusiveMaximum"] = true;
            }

            if (obj.TryGetPropertyValue("const", out var constNode))
            {
                obj.Remove("const");
                var enumArray = new JsonArray { constNode?.DeepClone() };
                obj["enum"] = enumArray;
            }

            if (obj.TryGetPropertyValue("examples", out var examplesNode))
            {
                obj.Remove("examples");
                if (examplesNode is JsonArray arr && arr.Count > 0 && !obj.ContainsKey("example"))
                {
                    obj["example"] = arr[0]?.DeepClone();
                }
            }

            foreach (var property in obj.ToList())
            {
                DowngradeSchemaFeatures(property.Value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var element in array)
            {
                DowngradeSchemaFeatures(element);
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