namespace HPD.Events;

/// <summary>
/// Event coordinator - manages event emission, streaming, and hierarchical bubbling.
/// Non-generic design works with any Event subclass without type conversions.
///
/// Key Features:
/// - Priority-based event routing (Upstream > Immediate > Control > Normal > Background)
/// - Hierarchical event bubbling via SetParent (child events bubble to parent)
/// - Bidirectional patterns (request/response with WaitForResponseAsync)
/// - Interruptible streams (group events that can be canceled together)
/// - Fire-and-forget emission (Emit) and ordered streaming (ReadAllAsync)
/// </summary>
public interface IEventCoordinator
{
    /// <summary>
    /// Emit an event downstream (fire-and-forget).
    /// Event is assigned a sequence number and routed to priority channel.
    /// If a parent coordinator is set, event bubbles up automatically.
    /// </summary>
    /// <param name="evt">Event to emit</param>
    void Emit(Event evt);

    /// <summary>
    /// Emit an event upstream (for interruption propagation).
    /// Used to propagate cancellations and interruptions up the hierarchy.
    /// </summary>
    /// <param name="evt">Event to emit upstream</param>
    void EmitUpstream(Event evt);

    /// <summary>
    /// Try to read a single event without blocking.
    /// Returns immediately with true if an event was available, false otherwise.
    /// Events are returned in priority order: Upstream > Immediate > Control > Normal > Background.
    /// </summary>
    /// <param name="evt">Output parameter containing the event if available</param>
    /// <returns>True if an event was read, false if no events available</returns>
    bool TryRead([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Event? evt);

    /// <summary>
    /// Read all events in priority order.
    /// Events are yielded in priority order: Upstream > Immediate > Control > Normal > Background.
    /// Blocks until events are available or cancellation is requested.
    /// </summary>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Async enumerable of events</returns>
    IAsyncEnumerable<Event> ReadAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Set parent coordinator for hierarchical event bubbling.
    /// Events emitted to this coordinator will automatically bubble to parent.
    /// No type conversions needed - all coordinators work with Event base class.
    /// </summary>
    /// <param name="parent">Parent coordinator to bubble events to</param>
    void SetParent(IEventCoordinator parent);

    /// <summary>
    /// Wait for a response event (bidirectional pattern).
    /// Used for request/response flows (e.g., permission requests, clarifications).
    /// Blocks until a response with matching requestId is received or timeout occurs.
    /// </summary>
    /// <typeparam name="TResponse">Expected response event type (must inherit from Event)</typeparam>
    /// <param name="requestId">Unique request ID to match response against</param>
    /// <param name="timeout">Maximum time to wait for response</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Response event of type TResponse</returns>
    /// <exception cref="TimeoutException">Thrown if no response received within timeout</exception>
    Task<TResponse> WaitForResponseAsync<TResponse>(
        string requestId,
        TimeSpan timeout,
        CancellationToken ct = default) where TResponse : Event;

    /// <summary>
    /// Send a response event (bidirectional pattern).
    /// Completes a pending WaitForResponseAsync call with matching requestId.
    /// </summary>
    /// <param name="requestId">Request ID this response corresponds to</param>
    /// <param name="response">Response event</param>
    void SendResponse(string requestId, Event response);

    /// <summary>
    /// Stream registry for managing interruptible event streams.
    /// Allows grouping events into streams that can be interrupted together.
    /// </summary>
    IStreamRegistry Streams { get; }
}
