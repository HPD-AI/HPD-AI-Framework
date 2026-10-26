namespace HPD.Agent.TUI.Composition;

public interface IAgentTuiStateStore
{
    T GetOrCreate<T>(string key, Func<T> create) where T : notnull;
    bool TryGet<T>(string key, out T value) where T : notnull;
    void Set<T>(string key, T value) where T : notnull;
    bool Remove(string key);
}

/// <summary>
/// Provides read-only access to process-owned services that are intentionally
/// shared across projection transactions.
/// </summary>
public sealed class AgentTuiSharedServices
{
    private readonly AgentTuiStateBag _state;

    internal AgentTuiSharedServices(AgentTuiStateBag state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public bool TryGet<T>(string key, out T value)
        where T : notnull
        => _state.TryGetShared(key, out value);
}

/// <summary>Creates an isolated working copy of one projection state value.</summary>
public interface IAgentTuiProjectionState
{
    object Fork();
}

public sealed class AgentTuiProjectionDictionary<TKey, TValue> : Dictionary<TKey, TValue>, IAgentTuiProjectionState
    where TKey : notnull
{
    public AgentTuiProjectionDictionary()
    {
    }

    public AgentTuiProjectionDictionary(IEqualityComparer<TKey> comparer)
        : base(comparer)
    {
    }

    public object Fork()
    {
        var fork = new AgentTuiProjectionDictionary<TKey, TValue>(Comparer);
        foreach (var pair in this)
            fork.Add(pair.Key, AgentTuiProjectionValueFork.Fork(pair.Value));
        return fork;
    }
}

public sealed class AgentTuiProjectionSet<T> : HashSet<T>, IAgentTuiProjectionState
{
    public AgentTuiProjectionSet(IEqualityComparer<T> comparer)
        : base(comparer)
    {
    }

    public object Fork()
    {
        var fork = new AgentTuiProjectionSet<T>(Comparer);
        fork.UnionWith(this);
        return fork;
    }
}

public sealed class AgentTuiStateBag : IAgentTuiStateStore
{
    private readonly Dictionary<string, object> _items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object> _services = new(StringComparer.Ordinal);

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

    /// <summary>
    /// Reads process-owned state without projection copying. This is intentionally
    /// unavailable through <see cref="IAgentTuiStateStore"/>.
    /// </summary>
    internal bool TryGetShared<T>(string key, out T value)
        where T : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (_services.TryGetValue(key, out var service) && service is T typed)
        {
            value = typed;
            return true;
        }

        value = default!;
        return false;
    }

    internal void SetSharedService<T>(string key, T service)
        where T : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(service);
        _services[key] = service;
    }

    internal bool RemoveSharedService(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return _services.Remove(key);
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

    internal AgentTuiProjectionStateStore BeginProjection()
        => new(this);

    internal void CommitProjection(IReadOnlyDictionary<string, object> writes, IReadOnlySet<string> removals)
    {
        foreach (var key in removals)
            _items.Remove(key);
        foreach (var pair in writes)
            _items[pair.Key] = pair.Value;
    }
}

internal sealed class AgentTuiProjectionStateStore : IAgentTuiStateStore
{
    private readonly AgentTuiStateBag _parent;
    private readonly Dictionary<string, object> _writes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _removals = new(StringComparer.Ordinal);

    public AgentTuiProjectionStateStore(AgentTuiStateBag parent)
    {
        _parent = parent;
    }

    public T GetOrCreate<T>(string key, Func<T> create)
        where T : notnull
    {
        return TryGet<T>(key, out var value) ? value : Stage(key, create());
    }

    public bool TryGet<T>(string key, out T value)
        where T : notnull
    {
        if (_writes.TryGetValue(key, out var staged))
        {
            value = staged is T typed
                ? typed
                : throw new InvalidOperationException(
                    $"State item '{key}' is registered as {staged.GetType().FullName}, not {typeof(T).FullName}.");
            return true;
        }

        if (!_parent.TryGet<T>(key, out var parentValue))
        {
            value = default!;
            return false;
        }

        value = Stage(key, AgentTuiStateFork.Fork(parentValue, key));
        return true;
    }

    public void Set<T>(string key, T value)
        where T : notnull
    {
        Stage(key, value);
    }

    public bool Remove(string key)
    {
        _writes.Remove(key);
        return _removals.Add(key);
    }

    internal void Commit()
        => _parent.CommitProjection(_writes, _removals);

    private T Stage<T>(string key, T value)
        where T : notnull
    {
        _writes[key] = value;
        _removals.Remove(key);
        return value;
    }
}

internal static class AgentTuiStateFork
{
    public static T Fork<T>(T value, string key)
        where T : notnull
    {
        if (typeof(T).IsValueType || value is string or Type or Delegate)
            return value;
        if (value is IAgentTuiProjectionState state)
            return (T)state.Fork();

        throw new InvalidOperationException(
            $"State item '{key}' uses mutable projection type {typeof(T).FullName}, but that type does not implement {nameof(IAgentTuiProjectionState)}.");
    }
}

internal static class AgentTuiProjectionValueFork
{
    public static TValue Fork<TValue>(TValue value)
    {
        if (value is null)
            return value;
        if (value is IAgentTuiProjectionState state)
            return (TValue)state.Fork();
        if (value is System.Text.StringBuilder text)
            return (TValue)(object)new System.Text.StringBuilder(text.ToString());
        return value;
    }
}
