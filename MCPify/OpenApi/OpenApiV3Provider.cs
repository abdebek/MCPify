using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Exceptions;
using Microsoft.OpenApi.Readers;
using Microsoft.OpenApi.Readers.Exceptions;
using MCPify.Core;
using System.Net;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;

namespace MCPify.OpenApi;

public class OpenApiV3Provider : IOpenApiProvider
{
    private readonly TimeSpan _timeout;
    private readonly SsrfGuard _ssrfGuard;
    private readonly IHttpClientFactory? _httpClientFactory;

    public OpenApiV3Provider(TimeSpan? timeout = null, SsrfGuard? ssrfGuard = null, IHttpClientFactory? httpClientFactory = null)
    {
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
        _ssrfGuard = ssrfGuard ?? new SsrfGuard();
        _httpClientFactory = httpClientFactory;
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
            ValidateUri(source);
            var httpClient = _httpClientFactory?.CreateClient() ?? new HttpClient { Timeout = _timeout };
            var disposeClient = _httpClientFactory == null;
            try
            {
                return await httpClient.GetStringAsync(source);
            }
            finally
            {
                if (disposeClient) httpClient.Dispose();
            }
        }

        return await File.ReadAllTextAsync(source);
    }

    private void ValidateUri(string source)
    {
        if (_ssrfGuard.DisableSsrfChecks) return;

        var uri = new Uri(source);

        if (uri.Scheme != "http" && uri.Scheme != "https")
        {
            throw new InvalidOperationException($"Only http and https schemes are allowed for OpenAPI URLs. Got: {uri.Scheme}");
        }

        var host = uri.Host;

        if (_ssrfGuard.BlockedHosts.Contains(host))
        {
            throw new InvalidOperationException($"Host '{host}' is blocked by SSRF guard (cloud metadata or explicitly blocked).");
        }

        if (!_ssrfGuard.AllowPrivateAddresses && IsPrivateOrLoopbackAddress(host))
        {
            throw new InvalidOperationException(
                $"Host '{host}' resolves to a private/loopback address. " +
                "Set McpifyOptions.SsrfGuard.AllowPrivateAddresses = true to allow internal addresses.");
        }
    }

    private static bool IsPrivateOrLoopbackAddress(string host)
    {
        if (IPAddress.TryParse(host, out var ip))
        {
            return IsPrivateIPAddress(ip);
        }

        try
        {
            var hostEntry = Dns.GetHostEntry(host);
            foreach (var address in hostEntry.AddressList)
            {
                if (IsPrivateIPAddress(address))
                {
                    return true;
                }
            }
        }
        catch
        {
            // DNS resolution failed — be conservative and block
            return true;
        }

        return false;
    }

    private static bool IsPrivateIPAddress(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;

        // IPv6 site-local / link-local
        if (ip.IsIPv6LinkLocal) return true;
        if (ip.IsIPv6SiteLocal) return true;

        // IPv4 private ranges (RFC1918) + link-local (169.254.0.0/16)
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            // 10.0.0.0/8
            if (bytes[0] == 10) return true;
            // 172.16.0.0/12 (172.16.x.x – 172.31.x.x)
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168) return true;
            // 169.254.0.0/16 link-local
            if (bytes[0] == 169 && bytes[1] == 254) return true;
        }

        return false;
    }

    private OpenApiDocument ParseWithFallback(string content)
    {
        try
        {
            return Parse(content);
        }
        catch (OpenApiUnsupportedSpecVersionException)
        {
            return Parse(DowngradeOpenApi31To30(content));
        }
        catch (OpenApiException)
        {
            // If it smells like 3.1, try downgrading
            if (content.Contains("\"openapi\": \"3.1") || content.Contains("\"openapi\":\"3.1"))
            {
                return Parse(DowngradeOpenApi31To30(content));
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
                // 1. Fix Root Version
                obj["openapi"] = "3.0.3";

                // 2. Remove 3.1-only Root Keywords
                obj.Remove("jsonSchemaDialect");
                obj.Remove("webhooks");

                // 3. Recursive Fixes
                DowngradeSchemaFeatures(obj);

                return obj.ToJsonString();
            }
        }
        catch
        {
            // Fallback for catastrophic JSON parse failure
        }
        return Regex.Replace(content, "\"openapi\"\\s*:\\s*\"3\\.1\\.[^\"]*\"", "\"openapi\": \"3.0.3\"", RegexOptions.IgnoreCase);
    }

    private static void DowngradeSchemaFeatures(JsonNode? node)
    {
        if (node == null) return;

        if (node is JsonObject obj)
        {
            // 3.1: "type": ["string", "null"] OR "type": ["integer", "string"]
            if (obj.TryGetPropertyValue("type", out var typeNode) && typeNode is JsonArray typeArray)
            {
                var types = typeArray.Select(x => x?.GetValue<string>()).Where(x => x != null).ToList();

                // Handle "null" in the array -> nullable: true
                if (types.Contains("null"))
                {
                    obj["nullable"] = true;
                    types.Remove("null");
                }

                // If only 1 type remains (e.g. "string"), set it as the single type
                if (types.Count == 1)
                {
                    obj["type"] = types[0];
                }
                // If multiple types remain (e.g. "integer", "string"), convert to 'oneOf'
                else if (types.Count > 1)
                {
                    obj.Remove("type"); // 3.0 doesn't allow array type

                    // Create oneOf: [ {type: int}, {type: string} ]
                    var oneOfArray = new JsonArray();
                    foreach (var t in types)
                    {
                        oneOfArray.Add(new JsonObject { ["type"] = t });
                    }

                    // If oneOf already exists, this is messy, but usually it doesn't.
                    // We'll prioritize our type conversion.
                    obj["oneOf"] = oneOfArray; 
                }
                else
                {
                    // It was just ["null"]? Remove type entirely (untyped nullable)
                    obj.Remove("type");
                }
            }

            // 3.1 tools often emit: anyOf: [ {type: string}, {type: null} ]
            CheckAndFixUnionNullability(obj, "anyOf");
            CheckAndFixUnionNullability(obj, "oneOf");

            // Exclusive Ranges (Number -> Boolean) ---
            ConvertExclusiveRange(obj, "exclusiveMinimum", "minimum");
            ConvertExclusiveRange(obj, "exclusiveMaximum", "maximum");

            // Const -> Enum ---
            if (obj.TryGetPropertyValue("const", out var constNode))
            {
                obj.Remove("const");
                obj["enum"] = new JsonArray { constNode?.DeepClone() };
            }

            //  Examples (Plural) -> Example (Singular) ---
            if (obj.TryGetPropertyValue("examples", out var examplesNode))
            {
                obj.Remove("examples");
                if (!obj.ContainsKey("example") && examplesNode is JsonArray arr && arr.Count > 0)
                {
                    obj["example"] = arr[0]?.DeepClone();
                }
            }

            // 3.1: contentEncoding: base64 -> 3.0: format: byte
            if (obj.TryGetPropertyValue("contentEncoding", out var encNode))
            {
                // If it is base64, map it to the standard OpenAPI 3.0 format "byte"
                if (encNode?.GetValue<string>() == "base64" && !obj.ContainsKey("format"))
                {
                    obj["format"] = "byte";
                }

                // Remove the property because it does not exist in OpenAPI 3.0 schemas
                obj.Remove("contentEncoding");
            }

            // Remove 3.1/JSON Schema Keywords that confuse parsers ---
            obj.Remove("$schema");
            obj.Remove("$id");
            obj.Remove("patternProperties");
            obj.Remove("unevaluatedProperties");

            // Recurse children
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

    private static void ConvertExclusiveRange(JsonObject obj, string exclusiveKey, string rangeKey)
    {
        if (obj.TryGetPropertyValue(exclusiveKey, out var exNode) && 
            exNode is JsonValue && 
            exNode.GetValueKind() == System.Text.Json.JsonValueKind.Number)
        {
            // 3.1: exclusiveMinimum: 10
            // 3.0: minimum: 10, exclusiveMinimum: true
            var val = exNode.GetValue<decimal>();
            obj[rangeKey] = val; // Set minimum/maximum to the value
            obj[exclusiveKey] = true; // Set exclusive flag to boolean true
        }
    }

    private static void CheckAndFixUnionNullability(JsonObject obj, string keyword)
    {
        if (obj.TryGetPropertyValue(keyword, out var node) && node is JsonArray arr && arr.Count == 2)
        {
            // Check for {type: null} in the pair
            var nullSchema = arr.FirstOrDefault(x => x is JsonObject o && o["type"]?.GetValue<string>() == "null");
            var otherSchema = arr.FirstOrDefault(x => x != nullSchema);

            if (nullSchema != null && otherSchema != null)
            {
                // We found a nullable union. Flatten it.
                obj.Remove(keyword);
                obj["nullable"] = true;

                // Merge properties from the 'other' schema into the parent object
                if (otherSchema is JsonObject otherObj)
                {
                    foreach (var prop in otherObj)
                    {
                        if (!obj.ContainsKey(prop.Key)) // Don't overwrite existing
                        {
                            obj[prop.Key] = prop.Value?.DeepClone();
                        }
                    }
                }
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