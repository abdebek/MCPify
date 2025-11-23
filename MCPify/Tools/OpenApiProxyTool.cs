using MCPify.Core;
using MCPify.Schema;
using Microsoft.OpenApi.Models;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Text;
using System.Text.Json;

namespace MCPify.Tools;

public class OpenApiProxyTool : McpServerTool
{
    private readonly HttpClient _http;
    private readonly IJsonSchemaGenerator _schema;
    private readonly string _apiBaseUrl;
    private readonly OpenApiOperationDescriptor _descriptor;

    public OpenApiProxyTool(
        OpenApiOperationDescriptor descriptor,
        string apiBaseUrl,
        HttpClient http,
        IJsonSchemaGenerator schema)
    {
        _descriptor = descriptor;
        _apiBaseUrl = apiBaseUrl.TrimEnd('/');
        _http = http;
        _schema = schema;
    }

    public override Tool ProtocolTool => new()
    {
        Name = _descriptor.Name,
        Description = _descriptor.Operation.Summary ?? $"Invoke {_descriptor.Method} {_descriptor.Route}",
        InputSchema = (JsonElement)JsonSerializer.SerializeToElement(_schema.GenerateInputSchema(_descriptor.Operation))
    };

    public override IReadOnlyList<object> Metadata => Array.Empty<object>();

    public override async ValueTask<CallToolResult> InvokeAsync(
        RequestContext<CallToolRequestParams> context,
        CancellationToken token)
    {
        var arguments = context.Params?.Arguments;
        var request = BuildHttpRequest(arguments);
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

    private HttpRequestMessage BuildHttpRequest(object? arguments)
    {
        var route = _descriptor.Route;
        var queryParams = new List<string>();
        object? bodyContent = null;

        if (arguments != null)
        {
            var argsDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                JsonSerializer.Serialize(arguments)
            ) ?? new Dictionary<string, JsonElement>();

            foreach (var param in _descriptor.Operation.Parameters ?? Enumerable.Empty<OpenApiParameter>())
            {
                if (!argsDict.TryGetValue(param.Name, out var value))
                    continue;

                var stringValue = value.ValueKind == JsonValueKind.String
                    ? value.GetString()
                    : value.ToString();

                switch (param.In)
                {
                    case ParameterLocation.Path:
                        route = route.Replace($"{{{param.Name}}}", Uri.EscapeDataString(stringValue ?? ""));
                        break;

                    case ParameterLocation.Query:
                        queryParams.Add($"{Uri.EscapeDataString(param.Name)}={Uri.EscapeDataString(stringValue ?? "")}");
                        break;

                    case ParameterLocation.Header:
                        break;
                }
            }

            if (argsDict.TryGetValue("body", out var bodyElement))
            {
                bodyContent = bodyElement;
            }
        }

        var url = _apiBaseUrl + route;
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

        return request;
    }
}
