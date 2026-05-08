namespace HPD.Events;

/// <summary>
/// Event coordinator - manages event emission, streaming, and hierarchical bubbling.
/// Non-generic design works with any Event subclass without type conversions.
///
/// Key Features:
/// - Channel-based event routing (Streaming, Synchronous, Interactive, Control)
/// - Hierarchical event bubbling via SetParent (child events bubble to parent)
/// - Bidirectional patterns (request/response with WaitForResponseAsync)
/// - Interruptible streams (group events that can be canceled together)
/// - Removable typed subscriptions and low-level channel readers
/// </summary>
public interface IEventCoordinator
{
    /// <summary>
    /// Emit an event downstream (fire-and-forget).
    /// Event is assigned a sequence number and routed to its declared channel.
    /// If a parent coordinator is set, event bubbles up automatically.
    /// </summary>
    /// <param name="evt">Event to emit</param>
    void Emit(Event evt);

    /// <summary>
    /// Emit an event asynchronously. Bounded Interactive and Control channels wait
    /// for capacity; Streaming drops oldest; Synchronous is unbounded.
    /// </summary>
    ValueTask EmitAsync(Event evt, CancellationToken ct = default);

    /// <summary>
    /// Register a removable typed handler for an exact class event type.
    /// </summary>
    IDisposable Subscribe<TEvent>(Func<TEvent, ValueTask> handler) where TEvent : Event;

    /// <summary>
    /// Register a removable broad observer that receives every class event from every channel.
    /// </summary>
    IDisposable SubscribeAny(Func<Event, ValueTask> handler);

    /// <summary>
    /// Try to emit a local struct event without waiting.
    /// </summary>
    bool TryEmitStruct<TEvent>(in TEvent evt) where TEvent : struct, IStructEvent;

    /// <summary>
    /// Emit a local struct event asynchronously, waiting when subscriber options request backpressure.
    /// </summary>
    ValueTask EmitStructAsync<TEvent>(TEvent evt, CancellationToken ct = default)
        where TEvent : struct, IStructEvent;

    /// <summary>
    /// Subscribe directly to a local struct event stream.
    /// </summary>
    StructSubscription<TEvent> SubscribeStruct<TEvent>(StructSubscriptionOptions? options = null)
        where TEvent : struct, IStructEvent;

    /// <summary>
    /// Register a removable handler for an exact local struct event type.
    /// </summary>
    IDisposable SubscribeStruct<TEvent>(Func<TEvent, ValueTask> handler)
        where TEvent : struct, IStructEvent;

    /// <summary>
    /// Create a pre-bound hot-path emitter for local struct events.
    /// </summary>
    StructEmitter<TEvent> CreateStructEmitter<TEvent>(StructEmitterOptions<TEvent>? options = null)
        where TEvent : struct, IStructEvent;

    /// <summary>
    /// Start all registered handlers. Runs one reader task per class-event channel
    /// plus registered struct-event handler pumps.
    /// </summary>
    Task RunAsync(CancellationToken ct = default);

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

    /// <summary>Returns current class-event channel depths.</summary>
    EventCoordinatorStats GetStats();

    /// <summary>Read streaming events directly.</summary>
    IAsyncEnumerable<Event> ReadStreamingAsync(CancellationToken ct = default);

    /// <summary>Read synchronous events directly.</summary>
    IAsyncEnumerable<Event> ReadSynchronousAsync(CancellationToken ct = default);

    /// <summary>Read interactive events directly.</summary>
    IAsyncEnumerable<Event> ReadInteractiveAsync(CancellationToken ct = default);

    /// <summary>Read control events directly.</summary>
    IAsyncEnumerable<Event> ReadControlAsync(CancellationToken ct = default);
}
