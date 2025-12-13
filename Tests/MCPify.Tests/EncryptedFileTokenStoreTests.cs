using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using MCPify.Core.Auth;
using MCPify.Core.Auth.OAuth;

namespace MCPify.Tests;

public class EncryptedFileTokenStoreTests : IDisposable
{
    private readonly string _tempPath;

    public EncryptedFileTokenStoreTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "MCPifyTokenStoreTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempPath);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsEncryptedToken()
    {
        var sessionId = "session-123";
        var provider = "OAuth";
        var token = new TokenData("access-token", "refresh-token", DateTimeOffset.UtcNow.AddMinutes(5));

        var store = new EncryptedFileTokenStore(_tempPath);
        await store.SaveTokenAsync(sessionId, provider, token);

        var tokenFile = Directory.GetFiles(_tempPath, "*.json", SearchOption.AllDirectories).Single();
        var ciphertext = await File.ReadAllBytesAsync(tokenFile);
        var plaintext = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(token));

        Assert.NotEmpty(ciphertext);
        Assert.False(ciphertext.SequenceEqual(plaintext)); // ensure not stored in plaintext

        var reloadedStore = new EncryptedFileTokenStore(_tempPath);
        var loaded = await reloadedStore.GetTokenAsync(sessionId, provider);

        Assert.NotNull(loaded);
        Assert.Equal(token.AccessToken, loaded!.AccessToken);
        Assert.Equal(token.RefreshToken, loaded.RefreshToken);
        Assert.Equal(token.ExpiresAt?.ToUnixTimeSeconds(), loaded.ExpiresAt?.ToUnixTimeSeconds());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath))
        {
            try
            {
                Directory.Delete(_tempPath, recursive: true);
            }
            catch
            {
                // best effort cleanup
            }
        }
    }
}
