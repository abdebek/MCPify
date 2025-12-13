using System.Collections.Concurrent;
using MCPify.Core.Auth;
using MCPify.Core.Auth.OAuth;

namespace MCPify.Tests;

public class InMemoryTokenStore : ISecureTokenStore
{
    private readonly ConcurrentDictionary<(string SessionId, string ProviderName), TokenData> _store = new();

    public Task<TokenData?> GetTokenAsync(string sessionId, string providerName, CancellationToken cancellationToken = default)
    {
        _store.TryGetValue((sessionId, providerName), out var token);
        return Task.FromResult(token);
    }

    public Task SaveTokenAsync(string sessionId, string providerName, TokenData token, CancellationToken cancellationToken = default)
    {
        _store[(sessionId, providerName)] = token;
        return Task.CompletedTask;
    }

    public Task DeleteTokenAsync(string sessionId, string providerName, CancellationToken cancellationToken = default)
    {
        _store.TryRemove((sessionId, providerName), out _);
        return Task.CompletedTask;
    }
}
