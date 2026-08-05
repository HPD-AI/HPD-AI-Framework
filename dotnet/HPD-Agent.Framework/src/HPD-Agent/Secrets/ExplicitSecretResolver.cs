namespace HPD.Agent.Secrets;

/// <summary>
/// Resolves secrets from explicitly provided key-value pairs.
/// Used when the user passes a value directly in code.
/// </summary>
public sealed class ExplicitSecretResolver : ISecretResolver
{
    private readonly Dictionary<string, string> _secrets;

    public ExplicitSecretResolver(IDictionary<string, string>? secrets = null)
    {
        _secrets = secrets != null
            ? new Dictionary<string, string>(secrets, StringComparer.Ordinal)
            : new(StringComparer.Ordinal);
    }

    /// <summary>Adds or replaces an explicit secret value.</summary>
    /// <param name="key">The canonical secret key.</param>
    /// <param name="value">The secret value.</param>
    public void Set(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        _secrets[key] = value;
    }

    public ValueTask<ResolvedSecret?> ResolveAsync(string key, CancellationToken ct = default)
    {
        if (_secrets.TryGetValue(key, out var value))
            return new(new ResolvedSecret { Value = value, Source = "explicit" });

        return default;
    }
}
