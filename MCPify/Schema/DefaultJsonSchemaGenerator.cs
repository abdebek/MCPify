using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using System.Text.Json;

namespace MCPify.Schema;

public class DefaultJsonSchemaGenerator : IJsonSchemaGenerator
{
    public object GenerateInputSchema(OpenApiOperation operation)
    {
        var properties = new Dictionary<string, object>();
        var required = new List<string>();

        foreach (var parameter in operation.Parameters ?? Enumerable.Empty<OpenApiParameter>())
        {
            var schemaObj = ConvertOpenApiSchemaToJsonSchema(parameter.Schema);

            if (schemaObj is Dictionary<string, object> dict)
            {
                if (!string.IsNullOrEmpty(parameter.Description))
                    dict["description"] = parameter.Description;

                dict["x-parameter-location"] = parameter.In.ToString().ToLowerInvariant();
            }

            properties[parameter.Name] = schemaObj;

            if (parameter.Required)
                required.Add(parameter.Name);
        }

        if (operation.RequestBody?.Content != null)
        {
            var firstContent = operation.RequestBody.Content.FirstOrDefault();
            if (firstContent.Value?.Schema != null)
            {
                var bodySchema = ConvertOpenApiSchemaToJsonSchema(firstContent.Value.Schema);
                if (bodySchema is Dictionary<string, object> bodyDict)
                {
                    if (!string.IsNullOrEmpty(operation.RequestBody.Description))
                        bodyDict["description"] = operation.RequestBody.Description;
                    if (!firstContent.Key.Equals("application/json", StringComparison.OrdinalIgnoreCase))
                        bodyDict["x-content-type"] = firstContent.Key;
                }
                properties["body"] = bodySchema;

                if (operation.RequestBody.Required)
                    required.Add("body");
            }
        }

        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["additionalProperties"] = false
        };

        if (required.Count > 0)
            schema["required"] = required;

        return schema;
    }

    public object? GenerateOutputSchema(OpenApiOperation operation)
    {
        var response = operation.Responses?.FirstOrDefault(r => r.Key.StartsWith("2"));
        if (response == null || response.Value.Value?.Content == null)
        {
            return null;
        }

        var firstContent = response.Value.Value.Content.FirstOrDefault();
        if (firstContent.Value?.Schema == null)
        {
            return null;
        }

        return ConvertOpenApiSchemaToJsonSchema(firstContent.Value.Schema);
    }

    private object ConvertOpenApiSchemaToJsonSchema(OpenApiSchema schema)
    {
        var result = new Dictionary<string, object>();

        if (schema.AllOf?.Count > 0)
        {
            result["allOf"] = schema.AllOf.Select(ConvertOpenApiSchemaToJsonSchema).ToList();
        }

        if (schema.OneOf?.Count > 0)
        {
            result["oneOf"] = schema.OneOf.Select(ConvertOpenApiSchemaToJsonSchema).ToList();
        }

        if (schema.AnyOf?.Count > 0)
        {
            result["anyOf"] = schema.AnyOf.Select(ConvertOpenApiSchemaToJsonSchema).ToList();
        }

        if (!string.IsNullOrEmpty(schema.Type))
        {
            result["type"] = schema.Type == "file" ? "string" : schema.Type;
        }

        if (schema.Nullable)
        {
            if (result.TryGetValue("type", out var typeVal) && typeVal is string ts)
                result["type"] = new[] { ts, "null" };
            else
                result["type"] = new[] { "null" };
        }

        if (!string.IsNullOrEmpty(schema.Format))
            result["format"] = schema.Format;

        if (!string.IsNullOrEmpty(schema.Description))
            result["description"] = schema.Description;

        if (schema.Default != null)
        {
            result["default"] = ConvertOpenApiAny(schema.Default);
        }

        if (schema.Enum?.Count > 0)
        {
            var enumValues = new List<object>();
            foreach (var item in schema.Enum)
                enumValues.Add(ConvertOpenApiAny(item));
            result["enum"] = enumValues;
        }

        if (schema.Properties?.Count > 0)
        {
            var properties = new Dictionary<string, object>();
            foreach (var prop in schema.Properties)
                properties[prop.Key] = ConvertOpenApiSchemaToJsonSchema(prop.Value);
            result["properties"] = properties;
            result["additionalProperties"] = false;
        }

        if (schema.Required?.Count > 0)
            result["required"] = schema.Required.ToList();

        if (schema.Items != null)
            result["items"] = ConvertOpenApiSchemaToJsonSchema(schema.Items);

        if (schema.MinItems.HasValue)
            result["minItems"] = schema.MinItems.Value;
        if (schema.MaxItems.HasValue)
            result["maxItems"] = schema.MaxItems.Value;

        if (schema.Minimum.HasValue)
            result["minimum"] = schema.Minimum.Value;
        if (schema.Maximum.HasValue)
            result["maximum"] = schema.Maximum.Value;

        if (schema.MinLength.HasValue)
            result["minLength"] = schema.MinLength.Value;
        if (schema.MaxLength.HasValue)
            result["maxLength"] = schema.MaxLength.Value;

        if (schema.Pattern != null)
            result["pattern"] = schema.Pattern;

        return result;
    }

    private static object ConvertOpenApiAny(IOpenApiAny any)
    {
        return any switch
        {
            OpenApiString s => (object)s.Value,
            OpenApiInteger i => i.Value,
            OpenApiLong l => l.Value,
            OpenApiDouble d => d.Value,
            OpenApiFloat f => f.Value,
            OpenApiBoolean b => b.Value,
            OpenApiNull => null!,
            _ => any.ToString() ?? ""
        };
    }
}