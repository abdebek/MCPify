namespace MCPify.Core.Auth;

public interface IAuthenticationProvider
{
    Task ApplyAsync(HttpRequestMessage request, CancellationToken cancellationToken = default);
}
