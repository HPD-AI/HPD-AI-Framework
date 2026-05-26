using System.Collections;

namespace Rhodium.Platform;

public readonly struct ParameterSet
{
    private readonly IReadOnlyDictionary<string, object> _values;
    private static readonly IReadOnlyDictionary<string, object> EmptyValues =
        new Dictionary<string, object>(StringComparer.Ordinal);

    public static ParameterSet Empty { get; } = new(EmptyValues);

    public ParameterSet(IReadOnlyDictionary<string, object> values)
    {
        _values = values;
    }

    public object this[string name] => Values[name];

    public bool TryGet(string name, out object? value) => Values.TryGetValue(name, out value);

    public T Get<T>(string name) => (T)Values[name];

    public T GetRequired<T>(string name, string propertyName)
    {
        if (!TryGet(name, out var value))
            throw new InvalidOperationException($"Parameter grid is missing value for strategy parameter '{name}'.");

        if (value is T typed)
            return typed;

        var actual = value?.GetType().Name ?? "<null>";
        throw new InvalidOperationException(
            $"Parameter grid value for '{name}' has type '{actual}', which cannot be assigned to strategy parameter '{propertyName}' of type '{typeof(T).Name}'.");
    }

    public IEnumerable<(string Name, object Value)> All
    {
        get
        {
            foreach (var item in Values)
                yield return (item.Key, item.Value);
        }
    }

    public int Count => Values.Count;

    public void CopyTo(Dictionary<string, object> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        foreach (var item in Values)
            destination[item.Key] = item.Value;
    }

    public void AddNamesTo(HashSet<string> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        foreach (var name in Values.Keys)
            destination.Add(name);
    }

    public void AddNamesTo(List<string> destination, HashSet<string> seen)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(seen);

        foreach (var name in Values.Keys)
        {
            if (seen.Add(name))
                destination.Add(name);
        }
    }

    private IReadOnlyDictionary<string, object> Values => _values ?? EmptyValues;
}
