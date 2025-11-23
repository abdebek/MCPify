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

            if (!string.IsNullOrEmpty(parameter.Description))
            {
                if (schemaObj is Dictionary<string, object> dict)
                {
                    dict["description"] = parameter.Description;
                }
            }

            properties[parameter.Name] = schemaObj;

            if (parameter.Required)
            {
                required.Add(parameter.Name);
            }
        }

        if (operation.RequestBody?.Content != null)
        {
            var firstContent = operation.RequestBody.Content.FirstOrDefault();
            if (firstContent.Value?.Schema != null)
            {
                var bodySchema = ConvertOpenApiSchemaToJsonSchema(firstContent.Value.Schema);
                properties["body"] = bodySchema;

                if (operation.RequestBody.Required)
                {
                    required.Add("body");
                }
            }
        }

        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = properties
        };

        if (required.Count > 0)
        {
            schema["required"] = required;
        }

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

        if (!string.IsNullOrEmpty(schema.Type))
        {
            result["type"] = schema.Type;
        }

        if (!string.IsNullOrEmpty(schema.Format))
        {
            result["format"] = schema.Format;
        }

        if (!string.IsNullOrEmpty(schema.Description))
        {
            result["description"] = schema.Description;
        }

        if (schema.Enum?.Count > 0)
        {
            result["enum"] = schema.Enum.Select(e => e.ToString()).ToList();
        }

        if (schema.Properties?.Count > 0)
        {
            var properties = new Dictionary<string, object>();
            foreach (var prop in schema.Properties)
            {
                properties[prop.Key] = ConvertOpenApiSchemaToJsonSchema(prop.Value);
            }
            result["properties"] = properties;
        }

        if (schema.Required?.Count > 0)
        {
            result["required"] = schema.Required.ToList();
        }

        if (schema.Items != null)
        {
            result["items"] = ConvertOpenApiSchemaToJsonSchema(schema.Items);
        }

        if (schema.Minimum.HasValue)
        {
            result["minimum"] = schema.Minimum.Value;
        }

        if (schema.Maximum.HasValue)
        {
            result["maximum"] = schema.Maximum.Value;
        }

        if (schema.MinLength.HasValue)
        {
            result["minLength"] = schema.MinLength.Value;
        }

        if (schema.MaxLength.HasValue)
        {
            result["maxLength"] = schema.MaxLength.Value;
        }

        if (schema.Pattern != null)
        {
            result["pattern"] = schema.Pattern;
        }

        return result;
    }
}
