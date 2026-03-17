namespace HPDAgent.Graph.Core.Channels;

using HPDAgent.Graph.Abstractions.Channels;

/// <summary>
/// Barrier channel - waits for N writes before allowing reads.
/// Useful for synchronization points in parallel execution.
/// Example: Wait for all parallel tasks to complete before proceeding.
/// </summary>
public class BarrierChannel<T> : IGraphChannel
{
    private readonly object _lock = new();
    private readonly List<T> _values = new();
    private readonly int _requiredCount;

    public string Name { get; }
    public int Version { get; private set; }
    public ChannelUpdateSemantics UpdateSemantics => ChannelUpdateSemantics.Barrier;

    /// <summary>
    /// Number of writes required before barrier is satisfied.
    /// </summary>
    public int RequiredCount => _requiredCount;

    /// <summary>
    /// Current number of writes received.
    /// </summary>
    public int CurrentCount
    {
        get
        {
            lock (_lock)
            {
                return _values.Count;
            }
        }
    }

    /// <summary>
    /// Whether the barrier has been satisfied (received N writes).
    /// </summary>
    public bool IsSatisfied
    {
        get
        {
            lock (_lock)
            {
                return _values.Count >= _requiredCount;
            }
        }
    }

    public BarrierChannel(string name, int requiredCount)
    {
        if (requiredCount <= 0)
            throw new ArgumentException("Required count must be greater than 0", nameof(requiredCount));

        Name = name ?? throw new ArgumentNullException(nameof(name));
        _requiredCount = requiredCount;
    }

    public TResult Get<TResult>()
    {
        lock (_lock)
        {
            // Wait until barrier is satisfied
            if (_values.Count < _requiredCount)
            {
                throw new InvalidOperationException(
                    $"Barrier not satisfied. Required: {_requiredCount}, Current: {_values.Count}");
            }

            // Return all collected values
            if (typeof(TResult) == typeof(List<T>))
            {
                return (TResult)(object)new List<T>(_values); // Defensive copy
            }

            throw new InvalidCastException(
                $"Cannot convert barrier channel values to {typeof(TResult).Name}. Use List<{typeof(T).Name}>");
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
            else
            {
                throw new ArgumentException(
                    $"Value type {typeof(TValue).Name} does not match channel type {typeof(T).Name}");
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
                else
                {
                    throw new ArgumentException(
                        $"Value type {typeof(TValue).Name} does not match channel type {typeof(T).Name}");
                }
            }
            Version++;
        }
    }

    /// <summary>
    /// Reset the barrier (clear collected values).
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _values.Clear();
            Version++;
        }
    }

    public void Clear()
    {
        // Clear is equivalent to Reset for barrier channels
        Reset();
    }
}
