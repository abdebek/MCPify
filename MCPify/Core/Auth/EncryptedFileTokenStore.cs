using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MCPify.Core.Auth.OAuth; // For TokenData

namespace MCPify.Core.Auth
{
public class EncryptedFileTokenStore : ISecureTokenStore
{
    private readonly string _basePath;
    // Entropy for ProtectedData. Protects against attacks where an attacker
    // tries to decrypt the data on a different machine or user account.
    private static readonly byte[] _entropy = Encoding.UTF8.GetBytes("MCPifySecureStorageEntropy");
    private const string KeyFileName = ".tokenstore.key";
    private readonly string? _encryptionKey;

    public EncryptedFileTokenStore(string basePath, string? encryptionKey = null)
    {
        if (string.IsNullOrWhiteSpace(basePath))
        {
            throw new ArgumentException("Base path cannot be null or empty.", nameof(basePath));
        }
        _basePath = basePath;
        _encryptionKey = encryptionKey;
    }

        private string GetSessionDirectory(string sessionId)
        {
            // Use a hash of the session ID to create a directory to avoid invalid path characters
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(sessionId));
                var hashedSessionId = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                return Path.Combine(_basePath, hashedSessionId);
            }
        }

        private string GetTokenFilePath(string sessionId, string providerName)
        {
            var sessionDir = GetSessionDirectory(sessionId);
            // Sanitize providerName for file system use
            var safeProviderName = SanitizeFileName(providerName);
            return Path.Combine(sessionDir, $"{safeProviderName}.json");
        }

        private string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        public async Task<TokenData?> GetTokenAsync(string sessionId, string providerName, CancellationToken cancellationToken = default)
        {
            var filePath = GetTokenFilePath(sessionId, providerName);

            if (!File.Exists(filePath))
            {
                return null;
            }

            try
            {
                var encryptedBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
                var decryptedBytes = Unprotect(encryptedBytes);
                var json = Encoding.UTF8.GetString(decryptedBytes);
                return JsonSerializer.Deserialize<TokenData>(json);
            }
            catch (CryptographicException ex)
            {
                // Log and return null if decryption fails (e.g., corrupted file, wrong entropy, different user)
                Console.Error.WriteLine($"Error decrypting token file for session {sessionId}, provider {providerName}: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error reading or deserializing token file for session {sessionId}, provider {providerName}: {ex.Message}");
                return null;
            }
        }

        public async Task SaveTokenAsync(string sessionId, string providerName, TokenData token, CancellationToken cancellationToken = default)
        {
            var filePath = GetTokenFilePath(sessionId, providerName);
            var sessionDir = Path.GetDirectoryName(filePath);

            if (!string.IsNullOrEmpty(sessionDir) && !Directory.Exists(sessionDir))
            {
                Directory.CreateDirectory(sessionDir);
            }

            var json = JsonSerializer.Serialize(token);
            var plaintextBytes = Encoding.UTF8.GetBytes(json);
            var encryptedBytes = Protect(plaintextBytes);

            await File.WriteAllBytesAsync(filePath, encryptedBytes, cancellationToken);
        }

        public Task DeleteTokenAsync(string sessionId, string providerName, CancellationToken cancellationToken = default)
        {
            var filePath = GetTokenFilePath(sessionId, providerName);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
            return Task.CompletedTask;
        }

        private byte[] Protect(byte[] data)
        {
            if (OperatingSystem.IsWindows())
            {
                return ProtectedData.Protect(data, _entropy, DataProtectionScope.CurrentUser);
            }

            var key = GetOrCreateKey();
            using var aes = Aes.Create();
            aes.Key = key;
            aes.GenerateIV();
            using var encryptor = aes.CreateEncryptor();
            using var ms = new MemoryStream();
            ms.Write(aes.IV, 0, aes.IV.Length);
            using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
            {
                cs.Write(data, 0, data.Length);
                cs.FlushFinalBlock();
            }
            return ms.ToArray();
        }

        private byte[] Unprotect(byte[] data)
        {
            if (OperatingSystem.IsWindows())
            {
                return ProtectedData.Unprotect(data, _entropy, DataProtectionScope.CurrentUser);
            }

            var key = GetOrCreateKey();
            using var aes = Aes.Create();
            aes.Key = key;
            var ivLength = aes.BlockSize / 8;
            var iv = data.AsSpan(0, ivLength).ToArray();
            var cipher = data.AsSpan(ivLength).ToArray();
            aes.IV = iv;
            using var decryptor = aes.CreateDecryptor();
            using var ms = new MemoryStream();
            using (var cs = new CryptoStream(new MemoryStream(cipher), decryptor, CryptoStreamMode.Read))
            {
                cs.CopyTo(ms);
            }
            return ms.ToArray();
        }

        private byte[] GetOrCreateKey()
        {
            if (!OperatingSystem.IsWindows() && string.IsNullOrWhiteSpace(_encryptionKey))
            {
                var keyFile = Path.Combine(_basePath, KeyFileName);
                if (File.Exists(keyFile))
                {
                    var raw = File.ReadAllText(keyFile).Trim();
                    return Convert.FromBase64String(raw);
                }

                var keyBytes = RandomNumberGenerator.GetBytes(32); // 256-bit
                var encoded = Convert.ToBase64String(keyBytes);
                Directory.CreateDirectory(_basePath);
                File.WriteAllText(keyFile, encoded);
                return keyBytes;
            }

            var keyMaterial = _encryptionKey ?? string.Empty;
            if (string.IsNullOrWhiteSpace(keyMaterial))
            {
                keyMaterial = Environment.GetEnvironmentVariable("MCPIFY_TOKENSTORE_KEY") ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(keyMaterial))
            {
                // Last-resort: generate ephemeral key per process
                return RandomNumberGenerator.GetBytes(32);
            }

            using var sha = SHA256.Create();
            return sha.ComputeHash(Encoding.UTF8.GetBytes(keyMaterial));
        }
    }
}
