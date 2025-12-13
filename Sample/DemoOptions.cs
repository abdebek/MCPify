namespace MCPify.Sample;

public class DemoOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5005";
    public bool EnableOAuth { get; set; } = true;
    public string OAuthBaseUrl { get; set; } = "http://localhost:5005";
    public string OAuthRedirectPath { get; set; } = "/auth/callback";
    public string StateSecret { get; set; } = "DEFAULT_INSECURE_SECRET_FOR_DEMO";
}