namespace HPD.Agent.Secrets;

internal sealed class ExplicitSecretResolver(IDictionary<string, string>? values = null) : ISecretResolver
{
    private readonly Dictionary<string, string> _values = values is null
        ? new(StringComparer.Ordinal)
        : new(values, StringComparer.Ordinal);

    internal void Set(string key, string value) => _values[key] = value;

    public ValueTask<ResolvedSecret?> ResolveAsync(string key, CancellationToken ct = default) =>
        new(_values.TryGetValue(key, out var value)
            ? new ResolvedSecret { Value = value, Source = "test" }
            : null);
}
