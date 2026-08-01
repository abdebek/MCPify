using System.Net.Http;

namespace MCPify.Core.Auth;

internal static class HttpClientFallback
{
    public static HttpClient Create(string callerName, TimeSpan? timeout = null)
    {
        Console.Error.WriteLine($"[MCPify] Warning: {callerName} was constructed without an HttpClient from IHttpClientFactory. Using a transient instance — this may cause socket exhaustion under load. Pass an HttpClient from IHttpClientFactory.CreateClient() for production use.");
        return timeout.HasValue ? new HttpClient { Timeout = timeout.Value } : new HttpClient();
    }
}