using System.Net.Http.Headers;

namespace MCPify.Core.Auth;

public class BearerAuthentication : IAuthenticationProvider
{
    public string Token { get; }

    public BearerAuthentication(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Token cannot be empty", nameof(token));
        Token = token;
    }

    public Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return Task.CompletedTask;
    }
}
