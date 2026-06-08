namespace HPD.Agent.TUI.Composition;

public sealed class AgentTuiStateBag
{
    private readonly Dictionary<string, object> _items = new(StringComparer.Ordinal);

    public T GetOrCreate<T>(string key, Func<T> create)
        where T : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(create);

        if (_items.TryGetValue(key, out var existing))
        {
            return existing is T typed
                ? typed
                : throw new InvalidOperationException(
                    $"State item '{key}' is registered as {existing.GetType().FullName}, not {typeof(T).FullName}.");
        }

        var value = create();
        _items[key] = value;
        return value;
    }

    public bool TryGet<T>(string key, out T value)
        where T : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (_items.TryGetValue(key, out var existing) && existing is T typed)
        {
            value = typed;
            return true;
        }

        value = default!;
        return false;
    }

    public void Set<T>(string key, T value)
        where T : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        _items[key] = value;
    }

    public bool Remove(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _items.Remove(key);
    }
}
