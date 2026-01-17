namespace MCPify.Core.Auth;

/// <summary>
/// Fluent helper for creating application/x-www-form-urlencoded POST content.
/// </summary>
internal class FormUrlEncoded
{
    private readonly List<KeyValuePair<string, string>> _params = new();

    public static FormUrlEncoded Create() => new();

    public FormUrlEncoded Add(string key, string value)
    {
        _params.Add(new(key, value));
        return this;
    }

    public FormUrlEncoded AddIfNotEmpty(string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            _params.Add(new(key, value));
        }
        return this;
    }

    public FormUrlEncodedContent ToContent() => new(_params);
}
