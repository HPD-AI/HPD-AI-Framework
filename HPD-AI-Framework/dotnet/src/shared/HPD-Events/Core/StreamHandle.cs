using System.Collections.Concurrent;

namespace HPD.Events.Core;

/// <summary>
/// Handle for controlling an interruptible event stream.
/// Implements IStreamHandle for lifecycle management of event streams.
/// </summary>
public sealed class StreamHandle : IStreamHandle
{
    private readonly IStreamRegistry _registry;
    private readonly TaskCompletionSource _completionTcs = new();
    private volatile bool _isInterrupted;
    private volatile bool _isCompleted;
    private int _emittedCount;
    private int _droppedCount;
    private bool _disposed;

    /// <summary>
    /// Create a new stream handle.
    /// </summary>
    /// <param name="streamId">Unique stream identifier</param>
    /// <param name="registry">Registry that owns this stream</param>
    internal StreamHandle(string streamId, IStreamRegistry registry)
    {
        StreamId = streamId;
        _registry = registry;
    }

    /// <inheritdoc />
    public string StreamId { get; }

    /// <inheritdoc />
    public bool IsInterrupted => _isInterrupted;

    /// <inheritdoc />
    public bool IsCompleted => _isCompleted;

    /// <inheritdoc />
    public int EmittedCount => _emittedCount;

    /// <inheritdoc />
    public int DroppedCount => _droppedCount;

    /// <inheritdoc />
    public event Action<IStreamHandle>? OnInterrupted;

    /// <inheritdoc />
    public event Action<IStreamHandle>? OnCompleted;

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

        // Complete stream on dispose (normal cleanup) BEFORE setting _disposed
        if (!IsInterrupted)
        {
            _registry.CompleteStream(StreamId);
        }

        _disposed = true;
    }
}
