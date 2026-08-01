using MCPify.Core;
using MCPify.Core.Auth;
using MCPify.Core.Auth.OAuth;
using MCPify.Schema;
using Microsoft.OpenApi.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MCPify.Tools;

public class OpenApiProxyTool : McpServerTool
{
    private readonly HttpClient _http;
    private readonly IJsonSchemaGenerator _schema;
    private readonly Func<string> _apiBaseUrlProvider;
    private readonly OpenApiOperationDescriptor _descriptor;
    private readonly McpifyOptions _options;
    private readonly ITokenProvider _tokenProvider;
    private IReadOnlyList<object>? _metadata;

    public OpenApiProxyTool(
        OpenApiOperationDescriptor descriptor,
        string apiBaseUrl,
        HttpClient http,
        IJsonSchemaGenerator schema,
        McpifyOptions options,
        ITokenProvider tokenProvider)
        : this(descriptor, () => apiBaseUrl, http, schema, options, tokenProvider)
    {
    }

    public OpenApiProxyTool(
        OpenApiOperationDescriptor descriptor,
        Func<string> apiBaseUrlProvider,
        HttpClient http,
        IJsonSchemaGenerator schema,
        McpifyOptions options,
        ITokenProvider tokenProvider)
    {
        _descriptor = descriptor;
        _apiBaseUrlProvider = apiBaseUrlProvider;
        _http = http;
        _schema = schema;
        _options = options;
        _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));
    }

    public override Tool ProtocolTool => new()
    {
        Name = _descriptor.Name,
        Description = _descriptor.Operation.Summary ?? $"Invoke {_descriptor.Method} {_descriptor.Route}",
        InputSchema = BuildInputSchema()
    };

    public override IReadOnlyList<object> Metadata => _metadata ??= BuildMetadata();

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> context,
        CancellationToken token)
    {
        try
        {
            var argsDict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            if (context.Params?.Arguments != null)
            {
                foreach (var entry in context.Params.Arguments)
                {
                    argsDict[entry.Key] = entry.Value.Clone();
                }
            }

            var request = BuildHttpRequest(argsDict);

            await _tokenProvider.ApplyAsync(request, token);

            var response = await _http.SendAsync(request, token);
            var content = await response.Content.ReadAsStringAsync(token);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = JsonSerializer.Serialize(new
                {
                    error = true,
                    statusCode = (int)response.StatusCode,
                    status = response.StatusCode.ToString(),
                    message = content
                });

                return new CallToolResult
                {
                    Content = new[] { new TextContentBlock { Text = errorContent } },
                    IsError = true
                };
            }

            return new CallToolResult
            {
                Content = new[] { new TextContentBlock { Text = content } }
            };
        }
        catch (ArgumentException ex)
        {
            return new CallToolResult
            {
                Content = new[] { new TextContentBlock { Text = ex.Message } },
                IsError = true
            };
        }
        catch (Exception ex)
        {
            return new CallToolResult
            {
                Content = new[] { new TextContentBlock { Text = $"Internal Error: {ex.Message}" } },
                IsError = true
            };
        }
    }

    private JsonElement BuildInputSchema()
    {
        var schemaNode = JsonSerializer.SerializeToNode(_schema.GenerateInputSchema(_descriptor.Operation)) ?? new JsonObject();
        return JsonSerializer.SerializeToElement(schemaNode);
    }

    private IReadOnlyList<object> BuildMetadata()
    {
        if (_descriptor.Operation.Security is null || _descriptor.Operation.Security.Count == 0)
        {
            return Array.Empty<object>();
        }

        var patterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var requirement in _descriptor.Operation.Security)
        {
            foreach (var scopes in requirement.Values)
            {
                if (scopes is null)
                {
                    continue;
                }

                foreach (var scope in scopes)
                {
                    if (string.IsNullOrWhiteSpace(scope))
                    {
                        continue;
                    }

                    patterns.Add(scope);
                }
            }
        }

        if (patterns.Count == 0)
        {
            return Array.Empty<object>();
        }

        return patterns.Select(pattern => (object)new ScopeRequirement(pattern)).ToArray();
    }

    private HttpRequestMessage BuildHttpRequest(Dictionary<string, JsonElement>? argsDict)
    {
        var route = _descriptor.Route;
        var queryParams = new List<string>();
        object? bodyContent = null;
        var headers = new Dictionary<string, string>();
        var missingPathParams = new List<string>();

        if (argsDict != null)
        {
            foreach (var param in _descriptor.Operation.Parameters ?? Enumerable.Empty<OpenApiParameter>())
            {
                if (!argsDict.TryGetValue(param.Name, out var value))
                {
                    if (param.In == ParameterLocation.Path && param.Required)
                    {
                        missingPathParams.Add(param.Name);
                    }
                    continue;
                }

                var stringValue = value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : value.ToString();

                switch (param.In)
                {
                    case ParameterLocation.Path:
                        if (string.IsNullOrWhiteSpace(stringValue))
                        {
                            if (param.Required)
                            {
                                missingPathParams.Add(param.Name);
                            }
                            break;
                        }
                        route = ReplaceRouteParameter(route, param.Name, Uri.EscapeDataString(stringValue ?? ""));
                        break;

                    case ParameterLocation.Query:
                        queryParams.Add($"{Uri.EscapeDataString(param.Name)}={Uri.EscapeDataString(stringValue ?? "")}");
                        break;

                    case ParameterLocation.Header:
                        if (!string.IsNullOrEmpty(stringValue))
                        {
                            headers[param.Name] = stringValue;
                        }
                        break;
                }
            }

            if (argsDict.TryGetValue("body", out var bodyElement))
            {
                bodyContent = bodyElement;
            }
        }

        if (missingPathParams.Count > 0)
        {
            throw new ArgumentException($"Missing required path parameter(s): {string.Join(", ", missingPathParams.Distinct(StringComparer.OrdinalIgnoreCase))}");
        }

        var unresolvedMatches = Regex.Matches(route, @"\{([^}:]+)(:[^}]+)?\}");
        if (unresolvedMatches.Count > 0)
        {
            var unresolved = unresolvedMatches
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            throw new ArgumentException($"Missing or invalid path parameter(s): {string.Join(", ", unresolved)}");
        }

        var baseUrl = _apiBaseUrlProvider().TrimEnd('/');
        var url = baseUrl + route;
        if (queryParams.Count > 0)
        {
            url += "?" + string.Join("&", queryParams);
        }

        var request = new HttpRequestMessage(new HttpMethod(_descriptor.Method.ToString()), url);

        if (bodyContent != null)
        {
            var jsonBody = JsonSerializer.Serialize(bodyContent);
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        foreach (var header in _options.DefaultHeaders)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var header in headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return request;
    }

    private static string ReplaceRouteParameter(string route, string paramName, string value)
    {
        route = route.Replace($"{{{paramName}}}", value, StringComparison.OrdinalIgnoreCase);

        var constrainedPattern = new Regex(@"\{" + Regex.Escape(paramName) + @":[^}]+\}", RegexOptions.IgnoreCase);
        if (constrainedPattern.IsMatch(route))
        {
            route = constrainedPattern.Replace(route, value);
        }

        return route;
    }
}
