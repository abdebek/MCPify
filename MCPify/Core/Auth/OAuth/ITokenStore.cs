namespace MCPify.Core.Auth.OAuth;

public interface ITokenStore
{
    Task<TokenData?> GetTokenAsync(CancellationToken cancellationToken = default);
    Task SaveTokenAsync(TokenData token, CancellationToken cancellationToken = default);
}
