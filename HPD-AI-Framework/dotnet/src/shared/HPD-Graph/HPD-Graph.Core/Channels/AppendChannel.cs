using HPDAgent.Graph.Abstractions.Channels;

namespace HPDAgent.Graph.Core.Channels;

/// <summary>
/// Channel that accumulates values into a list (append semantics).
/// Each Set() call appends to the list instead of replacing.
/// Prevents data loss in parallel execution.
/// Thread-safe implementation using lock-based synchronization.
/// </summary>
public sealed class AppendChannel<T> : IGraphChannel
{
    private readonly List<T> _values = new();
    private readonly object _lock = new();

    public string Name { get; }
    public int Version { get; private set; }
    public ChannelUpdateSemantics UpdateSemantics => ChannelUpdateSemantics.Append;

    public AppendChannel(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public TResult Get<TResult>()
    {
        lock (_lock)
        {
            // AppendChannel always returns List<T>
            if (typeof(TResult) == typeof(List<T>))
            {
                // Return defensive copy to prevent external modification
                return (TResult)(object)new List<T>(_values);
            }

            throw new InvalidOperationException(
                $"AppendChannel<{typeof(T).Name}> can only return List<{typeof(T).Name}>, " +
                $"but {typeof(TResult).Name} was requested.");
        }
    }

    public void Set<TValue>(TValue value)
    {
        lock (_lock)
        {
            if (value is T typedValue)
            {
                _values.Add(typedValue);
                Version++;
            }
            else if (value is IEnumerable<T> collection)
            {
                // If someone passes a collection, add all items
                _values.AddRange(collection);
                Version++;
            }
            else
            {
                throw new InvalidOperationException(
                    $"AppendChannel<{typeof(T).Name}> cannot accept value of type {typeof(TValue).Name}");
            }
        }
    }

    public void Update<TValue>(IEnumerable<TValue> values)
    {
        lock (_lock)
        {
            foreach (var value in values)
            {
                if (value is T typedValue)
                {
                    _values.Add(typedValue);
                }
            }
            Version++;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _values.Clear();
            Version++;
        }
    }
}
