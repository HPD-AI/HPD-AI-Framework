using System.Collections.Concurrent;

namespace HPD.Events.Core;

/// <summary>
/// Registry for managing interruptible event flows.
/// Thread-safe implementation using ConcurrentDictionary.
/// </summary>
public sealed class EventFlowRegistry : IEventFlowRegistry
{
    private readonly ConcurrentDictionary<string, EventFlowHandle> _activeFlows = new();

    /// <inheritdoc />
    public IEventFlowHandle Create(string? eventFlowId = null)
    {
        var id = eventFlowId ?? Guid.NewGuid().ToString("N");
        return BeginFlow(id);
    }

    /// <inheritdoc />
    public IEventFlowHandle BeginFlow(string eventFlowId)
    {
        var handle = new EventFlowHandle(eventFlowId, this);

        handle.OnCompleted += h =>
        {
            if (!h.IsInterrupted)
            {
                _activeFlows.TryRemove(h.EventFlowId, out _);
            }
        };

        if (!_activeFlows.TryAdd(eventFlowId, handle))
        {
            throw new InvalidOperationException($"Event flow with ID '{eventFlowId}' already exists");
        }

        return handle;
    }

    /// <inheritdoc />
    public IEventFlowHandle? Get(string eventFlowId)
    {
        _activeFlows.TryGetValue(eventFlowId, out var handle);
        return handle;
    }

    /// <inheritdoc />
    public void InterruptFlow(string eventFlowId)
    {
        if (_activeFlows.TryGetValue(eventFlowId, out var handle))
        {
            handle.Interrupt();
        }
    }

    /// <inheritdoc />
    public void CompleteFlow(string eventFlowId)
    {
        if (_activeFlows.TryGetValue(eventFlowId, out var handle))
        {
            handle.Complete();
            _activeFlows.TryRemove(eventFlowId, out _);
        }
    }

    /// <inheritdoc />
    public bool IsActive(string eventFlowId)
    {
        return _activeFlows.TryGetValue(eventFlowId, out var handle) && !handle.IsCompleted;
    }

    /// <inheritdoc />
    public void InterruptAll()
    {
        foreach (var handle in _activeFlows.Values.ToArray())
        {
            handle.Interrupt();
        }
    }

    /// <inheritdoc />
    public void InterruptWhere(Func<IEventFlowHandle, bool> predicate)
    {
        foreach (var handle in _activeFlows.Values.Where(predicate).ToArray())
        {
            handle.Interrupt();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<IEventFlowHandle> ActiveFlows =>
        _activeFlows.Values.Where(static handle => !handle.IsCompleted).ToList();

    /// <inheritdoc />
    public int ActiveCount => _activeFlows.Values.Count(static handle => !handle.IsCompleted);
}
