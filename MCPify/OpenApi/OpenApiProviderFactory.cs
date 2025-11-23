namespace MCPify.OpenApi;

public static class OpenApiProviderFactory
{
    public static IOpenApiProvider GetProvider(string? version = null)
    {
        return version switch
        {
            "2.0" => new OpenApiV3Provider(),
            "3.0" => new OpenApiV3Provider(),
            "3.1" => new OpenApiV3Provider(),
            _ => new OpenApiV3Provider()
        };
    }
}
