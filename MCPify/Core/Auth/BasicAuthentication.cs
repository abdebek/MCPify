using System.Net.Http.Headers;
using System.Text;

namespace MCPify.Core.Auth;

public class BasicAuthentication : IAuthenticationProvider
{
    public string Username { get; }
    public string Password { get; }

    public BasicAuthentication(string username, string password)
    {
        // Password can be empty
        if (username == null) throw new ArgumentNullException(nameof(username));
        if (password == null) throw new ArgumentNullException(nameof(password));

        Username = username;
        Password = password;
    }

    public Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken = default)
    {
        var credential = $"{Username}:{Password}";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(credential));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", encoded);
        return Task.CompletedTask;
    }
}
