namespace MCPify.Core.Auth.OAuth;

public class InMemoryTokenStore : ITokenStore
{
    private TokenData? _token;

    public Task<TokenData?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_token);
    }

    public Task SaveTokenAsync(TokenData token, CancellationToken cancellationToken = default)
    {
        _token = token;
        return Task.CompletedTask;
    }
}
