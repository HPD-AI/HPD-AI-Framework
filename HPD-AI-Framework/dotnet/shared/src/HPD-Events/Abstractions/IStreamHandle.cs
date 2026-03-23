namespace HPD.Events;

/// <summary>
/// Handle for controlling an interruptible event stream.
/// Returned by IStreamRegistry.BeginStream() for stream lifecycle management.
/// </summary>
public interface IStreamHandle : IDisposable
{
    /// <summary>
    /// Unique identifier for this stream.
    /// </summary>
    string StreamId { get; }

    /// <summary>
    /// Whether this stream has been interrupted.
    /// </summary>
    bool IsInterrupted { get; }

    /// <summary>
    /// Whether this stream has completed (either normally or via interruption).
    /// </summary>
    bool IsCompleted { get; }

    /// <summary>
    /// Number of events emitted on this stream.
    /// </summary>
    int EmittedCount { get; }

    /// <summary>
    /// Number of events dropped due to interruption.
    /// </summary>
    int DroppedCount { get; }

    /// <summary>
    /// Interrupt this stream.
    /// Events with CanInterrupt=true and matching StreamId will be dropped.
    /// </summary>
    void Interrupt();

    /// <summary>
    /// Complete this stream normally (no interruption).
    /// Removes stream from registry.
    /// </summary>
    void Complete();

    /// <summary>
    /// Wait for this stream to complete (either normally or via interruption).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task that completes when stream is completed</returns>
    Task WaitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when this stream is interrupted.
    /// </summary>
    event Action<IStreamHandle>? OnInterrupted;

    /// <summary>
    /// Event raised when this stream completes (either normally or via interruption).
    /// </summary>
    event Action<IStreamHandle>? OnCompleted;
}
