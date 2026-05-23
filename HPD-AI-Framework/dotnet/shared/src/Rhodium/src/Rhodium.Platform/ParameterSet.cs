using System.Collections;

namespace Rhodium.Platform;

public readonly struct ParameterSet
{
    private readonly IReadOnlyDictionary<string, object> _values;

    public ParameterSet(IReadOnlyDictionary<string, object> values)
    {
        _values = values;
    }

    public object this[string name] => _values[name];

    public bool TryGet(string name, out object? value) => _values.TryGetValue(name, out value);

    public T Get<T>(string name) => (T)_values[name];

    public IEnumerable<(string Name, object Value)> All
        => _values.Select(static kv => (kv.Key, kv.Value));
}
