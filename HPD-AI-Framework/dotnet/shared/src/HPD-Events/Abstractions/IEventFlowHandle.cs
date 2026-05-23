namespace HPD.Events;

/// <summary>
/// Handle for controlling an interruptible event flow.
/// </summary>
public interface IEventFlowHandle : IDisposable
{
    /// <summary>
    /// Unique identifier for this event flow.
    /// </summary>
    string EventFlowId { get; }

    /// <summary>
    /// Whether this event flow has been interrupted.
    /// </summary>
    bool IsInterrupted { get; }

    /// <summary>
    /// Whether this event flow has completed.
    /// </summary>
    bool IsCompleted { get; }

    /// <summary>
    /// Number of events emitted on this event flow.
    /// </summary>
    int EmittedCount { get; }

    /// <summary>
    /// Number of events dropped due to interruption.
    /// </summary>
    int DroppedCount { get; }

    /// <summary>
    /// Interrupt this event flow.
    /// Events with CanInterrupt=true and matching EventFlowId will be dropped.
    /// </summary>
    void Interrupt();

    /// <summary>
    /// Complete this event flow normally.
    /// </summary>
    void Complete();

    /// <summary>
    /// Wait for this event flow to complete.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task that completes when stream is completed</returns>
    Task WaitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event raised when this event flow is interrupted.
    /// </summary>
    event Action<IEventFlowHandle>? OnInterrupted;

    /// <summary>
    /// Event raised when this event flow completes.
    /// </summary>
    event Action<IEventFlowHandle>? OnCompleted;
}
