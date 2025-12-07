namespace MCPify.Core.Auth.OAuth;

public record TokenData(string AccessToken, string? RefreshToken, DateTimeOffset? ExpiresAt);
