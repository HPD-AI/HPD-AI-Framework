namespace HPD.Events;

/// <summary>
/// Registry for managing interruptible event streams.
/// Allows streams of events to be interrupted as a group (e.g., canceling an agent turn).
/// </summary>
public interface IStreamRegistry
{
    /// <summary>
    /// Create a new interruptible stream with optional auto-generated ID.
    /// Convenience method for when you don't need to specify a stream ID.
    /// </summary>
    /// <param name="streamId">Optional unique identifier. If null, generates a new GUID.</param>
    /// <returns>Handle for controlling the stream (interrupting, completing)</returns>
    IStreamHandle Create(string? streamId = null);

    /// <summary>
    /// Begin a new interruptible stream.
    /// Events emitted with this stream ID can be interrupted using the returned handle.
    /// </summary>
    /// <param name="streamId">Unique identifier for the stream</param>
    /// <returns>Handle for controlling the stream (interrupting, completing)</returns>
    IStreamHandle BeginStream(string streamId);

    /// <summary>
    /// Gets an existing stream handle by ID.
    /// </summary>
    /// <param name="streamId">Stream ID to retrieve</param>
    /// <returns>Stream handle if found, null otherwise</returns>
    IStreamHandle? Get(string streamId);

    /// <summary>
    /// Interrupt all events in the specified stream.
    /// Events with CanInterrupt=true and matching StreamId will be dropped.
    /// </summary>
    /// <param name="streamId">Stream ID to interrupt</param>
    void InterruptStream(string streamId);

    /// <summary>
    /// Complete a stream normally (no interruption).
    /// Removes stream from registry to free resources.
    /// </summary>
    /// <param name="streamId">Stream ID to complete</param>
    void CompleteStream(string streamId);

    /// <summary>
    /// Check if a stream is currently active.
    /// </summary>
    /// <param name="streamId">Stream ID to check</param>
    /// <returns>True if stream exists and is active</returns>
    bool IsActive(string streamId);

    /// <summary>
    /// Interrupt all active streams.
    /// </summary>
    void InterruptAll();

    /// <summary>
    /// Interrupt streams matching a predicate.
    /// </summary>
    /// <param name="predicate">Predicate to filter streams</param>
    void InterruptWhere(Func<IStreamHandle, bool> predicate);

    /// <summary>
    /// Gets all active (non-completed) streams.
    /// </summary>
    IReadOnlyList<IStreamHandle> ActiveStreams { get; }

    /// <summary>
    /// Gets the count of active streams.
    /// </summary>
    int ActiveCount { get; }
}
