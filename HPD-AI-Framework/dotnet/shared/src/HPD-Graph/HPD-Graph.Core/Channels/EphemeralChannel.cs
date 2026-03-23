namespace HPDAgent.Graph.Core.Channels;

using HPDAgent.Graph.Abstractions.Channels;

/// <summary>
/// Ephemeral channel - automatically cleared after each step.
/// Useful for temporary routing decisions and intermediate state.
/// NOT checkpointed (values don't persist across checkpoints).
/// </summary>
public class EphemeralChannel : IGraphChannel
{
    private readonly object _lock = new();
    private object? _value;
    private bool _hasValue;

    public string Name { get; }
    public int Version { get; private set; }
    public ChannelUpdateSemantics UpdateSemantics => ChannelUpdateSemantics.Ephemeral;

    public EphemeralChannel(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public T Get<T>()
    {
        lock (_lock)
        {
            if (!_hasValue)
            {
                return default!;
            }

            if (_value == null)
            {
                return default!;
            }

            if (_value is T typedValue)
            {
                return typedValue;
            }

            // Try to cast
            try
            {
                return (T)_value;
            }
            catch (InvalidCastException)
            {
                throw new InvalidCastException(
                    $"Cannot convert channel value of type {_value.GetType().Name} to {typeof(T).Name}");
            }
        }
    }

    public void Set<T>(T value)
    {
        lock (_lock)
        {
            _value = value;
            _hasValue = true;
            Version++;
        }
    }

    public void Update<T>(IEnumerable<T> values)
    {
        // For ephemeral channels, Update just sets to the last value
        lock (_lock)
        {
            var lastValue = values.LastOrDefault();
            _value = lastValue;
            _hasValue = true;
            Version++;
        }
    }

    /// <summary>
    /// Clear the channel value.
    /// Should be called after each execution step.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _value = null;
            _hasValue = false;
            Version++;
        }
    }

    /// <summary>
    /// Check if the channel has a value set.
    /// </summary>
    public bool HasValue
    {
        get
        {
            lock (_lock)
            {
                return _hasValue;
            }
        }
    }
}
