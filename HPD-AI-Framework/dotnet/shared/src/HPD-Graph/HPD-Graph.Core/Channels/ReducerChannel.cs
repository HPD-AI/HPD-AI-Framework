using HPDAgent.Graph.Abstractions.Channels;

namespace HPDAgent.Graph.Core.Channels;

/// <summary>
/// Channel with custom reducer function for merging values.
/// Enables complex aggregation logic (merge dictionaries, sum numbers, etc.).
/// Thread-safe implementation using lock-based synchronization.
/// </summary>
public sealed class ReducerChannel<T> : IGraphChannel
{
    private T _value;
    private readonly Func<T, T, T> _reducer;
    private readonly object _lock = new();

    public string Name { get; }
    public int Version { get; private set; }
    public ChannelUpdateSemantics UpdateSemantics => ChannelUpdateSemantics.Reducer;

    /// <summary>
    /// Creates a new reducer channel.
    /// </summary>
    /// <param name="name">Channel name</param>
    /// <param name="reducer">Merge function: (existing, incoming) => merged</param>
    /// <param name="initialValue">Initial value before any writes</param>
    public ReducerChannel(string name, Func<T, T, T> reducer, T initialValue = default!)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _reducer = reducer ?? throw new ArgumentNullException(nameof(reducer));
        _value = initialValue;
    }

    public TResult Get<TResult>()
    {
        lock (_lock)
        {
            if (typeof(TResult) == typeof(T))
            {
                return (TResult)(object)_value!;
            }

            throw new InvalidOperationException(
                $"ReducerChannel<{typeof(T).Name}> can only return {typeof(T).Name}, " +
                $"but {typeof(TResult).Name} was requested.");
        }
    }

    public void Set<TValue>(TValue value)
    {
        lock (_lock)
        {
            if (value is T typedValue)
            {
                _value = _reducer(_value, typedValue);
                Version++;
            }
            else
            {
                throw new InvalidOperationException(
                    $"ReducerChannel<{typeof(T).Name}> cannot accept value of type {typeof(TValue).Name}");
            }
        }
    }

    public void Update<TValue>(IEnumerable<TValue> values)
    {
        lock (_lock)
        {
            var hasChanges = false;
            foreach (var value in values)
            {
                if (value is T typedValue)
                {
                    _value = _reducer(_value, typedValue);
                    hasChanges = true;
                }
            }

            // Only increment version if at least one value was processed
            // Maintains consistency with Set() which increments once per call
            if (hasChanges)
            {
                Version++;
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _value = default!;
            Version++;
        }
    }
}
