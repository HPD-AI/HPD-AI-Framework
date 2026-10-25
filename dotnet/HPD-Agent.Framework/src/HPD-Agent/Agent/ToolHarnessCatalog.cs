using System.Collections;

namespace HPD.Agent;

/// <summary>Exact generated identities with short names used only as unambiguous aliases.</summary>
internal sealed class ToolHarnessCatalog : IReadOnlyDictionary<string, ToolHarnessFactory>
{
    private readonly Dictionary<string, ToolHarnessFactory> _identities = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, ToolHarnessFactory> _types = [];
    private readonly Dictionary<string, HashSet<string>> _aliases = new(StringComparer.OrdinalIgnoreCase);

    internal void Register(ToolHarnessFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var identity = factory.ActivationIdentity;
        if (_identities.TryGetValue(identity, out var existing))
        {
            if (!ReferenceEquals(existing, factory) && !existing.Equals(factory))
                throw new InvalidOperationException($"Conflicting ToolHarness registrations for identity '{identity}'.");
            return;
        }
        if (_types.TryGetValue(factory.ToolHarnessType, out existing))
            throw new InvalidOperationException(
                $"ToolHarness type '{factory.ToolHarnessType}' has conflicting identities '{existing.ActivationIdentity}' and '{identity}'.");

        _identities.Add(identity, factory);
        _types.Add(factory.ToolHarnessType, factory);
        if (!_aliases.TryGetValue(factory.Name, out var aliases))
            _aliases.Add(factory.Name, aliases = new(StringComparer.Ordinal));
        aliases.Add(identity);
    }

    internal bool TryGetByType(Type type, out ToolHarnessFactory factory) => _types.TryGetValue(type, out factory!);

    public bool TryGetValue(string key, out ToolHarnessFactory value)
    {
        if (_identities.TryGetValue(key, out value!)) return true;
        if (!_aliases.TryGetValue(key, out var aliases)) return false;
        if (aliases.Count != 1)
            throw new InvalidOperationException(
                $"ToolHarness name '{key}' is ambiguous. Use an exact type or stable identity: {string.Join(", ", aliases.Order(StringComparer.Ordinal))}.");
        value = _identities[aliases.Single()];
        return true;
    }

    public ToolHarnessFactory this[string key] => TryGetValue(key, out var factory)
        ? factory : throw new KeyNotFoundException($"ToolHarness '{key}' was not found.");
    public bool ContainsKey(string key) => TryGetValue(key, out _);
    public IEnumerable<string> Keys => _identities.Keys;
    public IEnumerable<ToolHarnessFactory> Values => _identities.Values;
    public int Count => _identities.Count;
    public IEnumerator<KeyValuePair<string, ToolHarnessFactory>> GetEnumerator() => _identities.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
