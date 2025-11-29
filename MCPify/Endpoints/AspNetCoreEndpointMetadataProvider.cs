using MCPify.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using System.Reflection;
using System.Text.RegularExpressions;

namespace MCPify.Endpoints;

public class AspNetCoreEndpointMetadataProvider : IEndpointMetadataProvider
{
    private readonly IServiceProvider _serviceProvider;

    public AspNetCoreEndpointMetadataProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public IEnumerable<OpenApiOperationDescriptor> GetLocalEndpoints()
    {
        var descriptors = new List<OpenApiOperationDescriptor>();
        var endpointSources = _serviceProvider.GetServices<EndpointDataSource>();

        foreach (var source in endpointSources)
        {
            foreach (var endpoint in source.Endpoints)
            {
                if (endpoint is RouteEndpoint routeEndpoint)
                {
                    var descriptor = CreateDescriptor(routeEndpoint);
                    if (descriptor != null)
                    {
                        descriptors.Add(descriptor);
                    }
                }
            }
        }

        return descriptors;
    }

    private OpenApiOperationDescriptor? CreateDescriptor(RouteEndpoint routeEndpoint)
    {
        var httpMethods = routeEndpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
        if (httpMethods == null || !httpMethods.Any())
        {
            return null;
        }

        var method = httpMethods.First();
        var route = "/" + routeEndpoint.RoutePattern.RawText?.TrimStart('/');

        var httpMethod = method.ToUpperInvariant() switch
        {
            "GET" => OperationType.Get,
            "POST" => OperationType.Post,
            "PUT" => OperationType.Put,
            "DELETE" => OperationType.Delete,
            "PATCH" => OperationType.Patch,
            _ => OperationType.Get
        };

        var toolName = GenerateToolName(method, route);
        var operation = BuildOpenApiOperation(routeEndpoint);

        return new OpenApiOperationDescriptor(toolName, route, httpMethod, operation);
    }

    private string GenerateToolName(string method, string route)
    {
        var normalized = route
            .Replace("/", "_")
            .Replace("{", "")
            .Replace("}", "")
            .Replace(":", "_");

        normalized = Regex.Replace(normalized, "[^a-zA-Z0-9_]", "");
        normalized = Regex.Replace(normalized, "_+", "_");
        normalized = normalized.Trim('_');

        var parts = normalized.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var snakeCase = string.Join("_", parts.Select(p => p.ToLowerInvariant()));

        return $"{method.ToLowerInvariant()}_{snakeCase}";
    }

    private OpenApiOperation BuildOpenApiOperation(RouteEndpoint routeEndpoint)
    {
        var operation = new OpenApiOperation
        {
            Summary = routeEndpoint.DisplayName ?? "Endpoint",
            Parameters = new List<OpenApiParameter>()
        };

        foreach (var parameter in routeEndpoint.RoutePattern.Parameters)
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = parameter.Name,
                In = ParameterLocation.Path,
                Required = true,
                Schema = new OpenApiSchema { Type = "string" }
            });
        }

        var methodInfo = routeEndpoint.Metadata.GetMetadata<MethodInfo>();
        if (methodInfo != null)
        {
            var parameters = methodInfo.GetParameters();

            foreach (var param in parameters)
            {
                if (IsSpecialParameter(param.ParameterType))
                {
                    continue;
                }

                if (routeEndpoint.RoutePattern.Parameters.Any(p =>
                    p.Name.Equals(param.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (param.GetCustomAttribute<Microsoft.AspNetCore.Mvc.FromBodyAttribute>() != null ||
                    IsComplexType(param.ParameterType))
                {
                    operation.RequestBody = new OpenApiRequestBody
                    {
                        Required = !IsNullable(param),
                        Content = new Dictionary<string, OpenApiMediaType>
                        {
                            ["application/json"] = new OpenApiMediaType
                            {
                                Schema = InferSchema(param.ParameterType)
                            }
                        }
                    };
                }
                else if (param.GetCustomAttribute<Microsoft.AspNetCore.Mvc.FromQueryAttribute>() != null ||
                         IsPrimitiveType(param.ParameterType))
                {
                    operation.Parameters.Add(new OpenApiParameter
                    {
                        Name = param.Name ?? "parameter",
                        In = ParameterLocation.Query,
                        Required = !IsNullable(param),
                        Schema = InferSchema(param.ParameterType)
                    });
                }
            }
        }

        return operation;
    }

    private bool IsSpecialParameter(Type type)
    {
        return type == typeof(HttpContext) ||
               type == typeof(HttpRequest) ||
               type == typeof(HttpResponse) ||
               type == typeof(CancellationToken) ||
               type.IsAssignableTo(typeof(System.Security.Claims.ClaimsPrincipal));
    }

    private bool IsNullable(ParameterInfo param)
    {
        var nullabilityContext = new NullabilityInfoContext();
        var nullabilityInfo = nullabilityContext.Create(param);
        return nullabilityInfo.ReadState == NullabilityState.Nullable;
    }

    private bool IsPrimitiveType(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        return underlyingType.IsPrimitive ||
               underlyingType == typeof(string) ||
               underlyingType == typeof(decimal) ||
               underlyingType == typeof(DateTime) ||
               underlyingType == typeof(DateTimeOffset) ||
               underlyingType == typeof(TimeSpan) ||
               underlyingType == typeof(Guid);
    }

    private bool IsComplexType(Type type)
    {
        return !IsPrimitiveType(type) && !type.IsEnum;
    }

    private OpenApiSchema InferSchema(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

        if (underlyingType == typeof(int) || underlyingType == typeof(long) ||
            underlyingType == typeof(short) || underlyingType == typeof(byte))
        {
            return new OpenApiSchema { Type = "integer" };
        }

        if (underlyingType == typeof(float) || underlyingType == typeof(double) ||
            underlyingType == typeof(decimal))
        {
            return new OpenApiSchema { Type = "number" };
        }

        if (underlyingType == typeof(bool))
        {
            return new OpenApiSchema { Type = "boolean" };
        }

        if (underlyingType == typeof(string) || underlyingType == typeof(Guid) ||
            underlyingType == typeof(DateTime) || underlyingType == typeof(DateTimeOffset))
        {
            return new OpenApiSchema { Type = "string" };
        }

        if (underlyingType.IsEnum)
        {
            return new OpenApiSchema
            {
                Type = "string",
                Enum = Enum.GetNames(underlyingType)
                    .Select(name => (Microsoft.OpenApi.Any.IOpenApiAny)new Microsoft.OpenApi.Any.OpenApiString(name))
                    .ToList()
            };
        }

        if (underlyingType.IsArray)
        {
            return new OpenApiSchema
            {
                Type = "array",
                Items = InferSchema(underlyingType.GetElementType()!)
            };
        }

        if (underlyingType.IsGenericType &&
            (underlyingType.GetGenericTypeDefinition() == typeof(List<>) ||
             underlyingType.GetGenericTypeDefinition() == typeof(IEnumerable<>) ||
             underlyingType.GetGenericTypeDefinition() == typeof(ICollection<>)))
        {
            return new OpenApiSchema
            {
                Type = "array",
                Items = InferSchema(underlyingType.GetGenericArguments()[0])
            };
        }

        var schema = new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>(),
            Required = new HashSet<string>()
        };

        var properties = underlyingType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        foreach (var prop in properties)
        {
            schema.Properties[prop.Name] = InferSchema(prop.PropertyType);

            var nullabilityContext = new NullabilityInfoContext();
            var nullabilityInfo = nullabilityContext.Create(prop);
            if (nullabilityInfo.WriteState != NullabilityState.Nullable)
            {
                schema.Required.Add(prop.Name);
            }
        }

        return schema;
    }
}
