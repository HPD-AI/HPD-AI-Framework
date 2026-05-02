using System.Collections.Concurrent;

namespace HPD.Events.Core;

/// <summary>
/// Registry for managing interruptible event streams.
/// Thread-safe implementation using ConcurrentDictionary.
/// </summary>
public sealed class StreamRegistry : IStreamRegistry
{
    private readonly ConcurrentDictionary<string, StreamHandle> _activeStreams = new();

    /// <inheritdoc />
    public IStreamHandle Create(string? streamId = null)
    {
        var id = streamId ?? Guid.NewGuid().ToString("N");
        return BeginStream(id);
    }

    /// <inheritdoc />
    public IStreamHandle BeginStream(string streamId)
    {
        var handle = new StreamHandle(streamId, this);

        // Auto-remove stream from registry when completed ONLY if not interrupted
        // Interrupted streams stay in registry so EventCoordinator.Emit() can check IsInterrupted
        handle.OnCompleted += h =>
        {
            if (!h.IsInterrupted)
            {
                _activeStreams.TryRemove(h.StreamId, out _);
            }
        };

        if (!_activeStreams.TryAdd(streamId, handle))
        {
            throw new InvalidOperationException($"Stream with ID '{streamId}' already exists");
        }

        return handle;
    }

    /// <inheritdoc />
    public IStreamHandle? Get(string streamId)
    {
        _activeStreams.TryGetValue(streamId, out var handle);
        return handle;
    }

    /// <inheritdoc />
    public void InterruptStream(string streamId)
    {
        if (_activeStreams.TryGetValue(streamId, out var handle))
        {
            handle.Interrupt();
        }
    }

    /// <inheritdoc />
    public void CompleteStream(string streamId)
    {
        if (_activeStreams.TryGetValue(streamId, out var handle))
        {
            handle.Complete();
            _activeStreams.TryRemove(streamId, out _);
        }
    }

    /// <inheritdoc />
    public bool IsActive(string streamId)
    {
        return _activeStreams.TryGetValue(streamId, out var handle) && !handle.IsCompleted;
    }

    /// <inheritdoc />
    public void InterruptAll()
    {
        foreach (var handle in _activeStreams.Values.ToArray())
        {
            handle.Interrupt();
        }
    }

    /// <inheritdoc />
    public void InterruptWhere(Func<IStreamHandle, bool> predicate)
    {
        foreach (var handle in _activeStreams.Values.Where(predicate).ToArray())
        {
            handle.Interrupt();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<IStreamHandle> ActiveStreams =>
        _activeStreams.Values.Where(static handle => !handle.IsCompleted).ToList();

    /// <inheritdoc />
    public int ActiveCount => _activeStreams.Values.Count(static handle => !handle.IsCompleted);
}
