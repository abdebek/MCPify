namespace MCPify.Core.Auth;

public class OAuthConfigurationStore
{
    private readonly List<OAuth2Configuration> _configurations = new();
    private readonly object _lock = new();

    public void AddConfiguration(OAuth2Configuration config)
    {
        lock (_lock)
        {
            _configurations.Add(config);
        }
    }

    public IEnumerable<OAuth2Configuration> GetConfigurations()
    {
        lock (_lock)
        {
            return _configurations.ToList();
        }
    }
}
