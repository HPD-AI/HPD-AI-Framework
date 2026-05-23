using System.Collections.Concurrent;

namespace HPD.Events.Core;

/// <summary>
/// Handle for controlling an interruptible event flow.
/// </summary>
public sealed class EventFlowHandle : IEventFlowHandle
{
    private readonly IEventFlowRegistry _registry;
    private readonly TaskCompletionSource _completionTcs = new();
    private volatile bool _isInterrupted;
    private volatile bool _isCompleted;
    private int _emittedCount;
    private int _droppedCount;
    private bool _disposed;

    /// <summary>
    /// Create a new event-flow handle.
    /// </summary>
    /// <param name="eventFlowId">Unique event flow identifier.</param>
    /// <param name="registry">Registry that owns this event flow.</param>
    internal EventFlowHandle(string eventFlowId, IEventFlowRegistry registry)
    {
        EventFlowId = eventFlowId;
        _registry = registry;
    }

    /// <inheritdoc />
    public string EventFlowId { get; }

    /// <inheritdoc />
    public bool IsInterrupted => _isInterrupted;

    /// <inheritdoc />
    public bool IsCompleted => _isCompleted;

    /// <inheritdoc />
    public int EmittedCount => _emittedCount;

    /// <inheritdoc />
    public int DroppedCount => _droppedCount;

    /// <inheritdoc />
    public event Action<IEventFlowHandle>? OnInterrupted;

    /// <inheritdoc />
    public event Action<IEventFlowHandle>? OnCompleted;

    /// <summary>
    /// Increment emitted count (called by coordinator when event is emitted).
    /// </summary>
    public void IncrementEmittedCount() => Interlocked.Increment(ref _emittedCount);

    /// <summary>
    /// Increment dropped count (called by coordinator when event is dropped).
    /// </summary>
    public void IncrementDroppedCount() => Interlocked.Increment(ref _droppedCount);

    /// <inheritdoc />
    public void Interrupt()
    {
        if (_isCompleted)
            return;

        _isInterrupted = true;
        OnInterrupted?.Invoke(this);
        Complete();
    }

    /// <inheritdoc />
    public void Complete()
    {
        if (_isCompleted)
            return;

        _isCompleted = true;
        _completionTcs.TrySetResult();
        OnCompleted?.Invoke(this);
    }

    /// <inheritdoc />
    public Task WaitAsync(CancellationToken cancellationToken = default)
    {
        return _completionTcs.Task.WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        if (!IsInterrupted)
        {
            _registry.CompleteFlow(EventFlowId);
        }

        _disposed = true;
    }
}
