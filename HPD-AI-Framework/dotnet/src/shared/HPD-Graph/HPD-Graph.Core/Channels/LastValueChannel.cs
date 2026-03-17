using HPDAgent.Graph.Abstractions.Channels;

namespace HPDAgent.Graph.Core.Channels;

/// <summary>
/// Channel that stores a single value (last write wins).
/// Thread-safe implementation using lock-based synchronization.
/// </summary>
public sealed class LastValueChannel : IGraphChannel
{
    private object? _value;
    private readonly object _lock = new();

    public string Name { get; }
    public int Version { get; private set; }
    public ChannelUpdateSemantics UpdateSemantics => ChannelUpdateSemantics.LastValue;

    public LastValueChannel(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public T Get<T>()
    {
        lock (_lock)
        {
            if (_value == null)
            {
                return default!;
            }

            if (_value is T typedValue)
            {
                return typedValue;
            }

            throw new InvalidOperationException(
                $"Channel '{Name}' contains value of type {_value.GetType().Name}, " +
                $"but {typeof(T).Name} was requested.");
        }
    }

    public void Set<T>(T value)
    {
        lock (_lock)
        {
            _value = value;
            Version++;
        }
    }

    public void Update<T>(IEnumerable<T> values)
    {
        throw new NotSupportedException(
            $"LastValueChannel does not support Update with multiple values. Use Set instead.");
    }

    public void Clear()
    {
        lock (_lock)
        {
            _value = default;
            Version++;
        }
    }
}
