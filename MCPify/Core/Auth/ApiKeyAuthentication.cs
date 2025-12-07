using System.Web;

namespace MCPify.Core.Auth;

public enum ApiKeyLocation
{
    Header,
    Query
}

public class ApiKeyAuthentication : IAuthenticationProvider
{
    public string KeyName { get; }
    public string KeyValue { get; }
    public ApiKeyLocation Location { get; }

    public ApiKeyAuthentication(string keyName, string keyValue, ApiKeyLocation location = ApiKeyLocation.Header)
    {
        if (string.IsNullOrWhiteSpace(keyName)) throw new ArgumentException("Key name cannot be empty", nameof(keyName));
        if (string.IsNullOrWhiteSpace(keyValue)) throw new ArgumentException("Key value cannot be empty", nameof(keyValue));

        KeyName = keyName;
        KeyValue = keyValue;
        Location = location;
    }

    public Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        if (Location == ApiKeyLocation.Header)
        {
            request.Headers.TryAddWithoutValidation(KeyName, KeyValue);
        }
        else if (Location == ApiKeyLocation.Query)
        {
            var uriBuilder = new UriBuilder(request.RequestUri!);
            var query = HttpUtility.ParseQueryString(uriBuilder.Query);
            query[KeyName] = KeyValue;
            uriBuilder.Query = query.ToString();
            request.RequestUri = uriBuilder.Uri;
        }

        return Task.CompletedTask;
    }
}
