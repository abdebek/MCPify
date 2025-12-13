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

        public EncryptedFileTokenStore(string basePath)
        {
            if (string.IsNullOrWhiteSpace(basePath))
            {
                throw new ArgumentException("Base path cannot be null or empty.", nameof(basePath));
            }
            _basePath = basePath;
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
                // ProtectedData.Unprotect is Windows-specific for DataProtectionScope.CurrentUser
                // For cross-platform, Microsoft.AspNetCore.DataProtection is recommended.
                var decryptedBytes = System.Security.Cryptography.ProtectedData.Unprotect(encryptedBytes, _entropy, System.Security.Cryptography.DataProtectionScope.CurrentUser);
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
            // ProtectedData.Protect is Windows-specific for DataProtectionScope.CurrentUser
            // For cross-platform, Microsoft.AspNetCore.DataProtection is recommended.
            var encryptedBytes = System.Security.Cryptography.ProtectedData.Protect(plaintextBytes, _entropy, System.Security.Cryptography.DataProtectionScope.CurrentUser);

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
    }
}
