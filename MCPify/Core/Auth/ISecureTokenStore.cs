using System.Threading;
using System.Threading.Tasks;
using MCPify.Core.Auth.OAuth;

namespace MCPify.Core.Auth
{
    public interface ISecureTokenStore
    {
        // Retrieves token data for a specific session and authentication provider
        Task<TokenData?> GetTokenAsync(string sessionId, string providerName, CancellationToken cancellationToken = default);

        // Saves token data for a specific session and authentication provider
        Task SaveTokenAsync(string sessionId, string providerName, TokenData token, CancellationToken cancellationToken = default);

        // Deletes token data for a specific session and authentication provider
        Task DeleteTokenAsync(string sessionId, string providerName, CancellationToken cancellationToken = default);
    }
}
