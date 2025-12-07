using System.Text.Json;

namespace MCPify.Core.Auth.OAuth;

public class FileTokenStore : ITokenStore
{
    private readonly string _filePath;

    public FileTokenStore(string filePath)
    {
        _filePath = filePath;
    }

    public async Task<TokenData?> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath)) return null;
        try
        {
            using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<TokenData>(stream, cancellationToken: cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveTokenAsync(TokenData token, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        
        using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, token, cancellationToken: cancellationToken);
    }
}
