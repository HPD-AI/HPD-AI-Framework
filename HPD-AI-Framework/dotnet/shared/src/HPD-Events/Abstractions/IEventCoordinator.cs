using System.Threading.Channels;

namespace HPD.Events;

/// <summary>
/// Event coordinator - manages event publication, streaming, and hierarchical bubbling.
/// Non-generic design works with any Event subclass without type conversions.
///
/// Events are delivered through per-subscriber mailboxes. Handler subscriptions are
/// processed by background pumps, so publishing an event does not mean every handler
/// has finished running.
///
/// Key Features:
/// - Fan-out event routing with per-subscriber mailboxes
/// - Hierarchical event bubbling via SetParent (child events bubble to parent)
/// - Bidirectional request/response with RequestAsync and Respond/TryRespond
/// - Interruptible streams (group events that can be canceled together)
/// - Removable typed, catch-all, stream, and channel subscriptions
/// </summary>
public interface IEventCoordinator
{
    /// <summary>
    /// Process-local high-throughput struct event lanes.
    /// </summary>
    ILocalStructEventBus LocalStructs { get; }

    /// <summary>
    /// Publish an event downstream without waiting for subscriber mailbox capacity.
    /// The event is assigned a sequence number and routed to matching subscriber mailboxes.
    /// If a parent coordinator is set, event bubbles up automatically.
    /// </summary>
    /// <remarks>
    /// This method only publishes into subscriber mailboxes. Handlers registered with
    /// <see cref="Subscribe{TEvent}"/> or <see cref="SubscribeAny"/> run on their
    /// own pumps and may still be executing after this method returns.
    /// </remarks>
    /// <param name="evt">Event to emit</param>
    void Emit(Event evt);

    /// <summary>
    /// Publish an event asynchronously, waiting only when subscriber mailboxes request
    /// backpressure.
    /// </summary>
    /// <remarks>
    /// Awaiting this method means the event was accepted by matching subscriber mailboxes
    /// (and parent coordinators) according to their backpressure settings. It does not
    /// wait for handler callbacks to finish processing the event. Use
    /// <see cref="RequestAsync{TRequest,TResponse}"/> or a synchronous application path
    /// when the caller must observe handler completion.
    /// </remarks>
    ValueTask EmitAsync(Event evt, CancellationToken ct = default);

    /// <summary>
    /// Register a removable typed handler processed by a background subscriber pump.
    /// </summary>
    /// <remarks>
    /// Do not capture request-scoped services in long-lived subscriptions unless the
    /// subscription lifetime is tied to that request. For work that uses scoped services,
    /// create a scope inside the handler or delegate to a scoped worker.
    /// </remarks>
    IDisposable Subscribe<TEvent>(
        Func<TEvent, ValueTask> handler,
        EventSubscriptionOptions? options = null)
        where TEvent : Event;

    /// <summary>
    /// Register a removable broad observer that receives every class event from every channel
    /// on a background subscriber pump.
    /// </summary>
    /// <remarks>
    /// Do not capture request-scoped services in long-lived subscriptions unless the
    /// subscription lifetime is tied to that request. For work that uses scoped services,
    /// create a scope inside the handler or delegate to a scoped worker.
    /// </remarks>
    IDisposable SubscribeAny(
        Func<Event, ValueTask> handler,
        EventSubscriptionOptions? options = null);

    /// <summary>
    /// Create a caller-owned typed class-event inbox.
    /// </summary>
    EventInbox<TEvent> CreateInbox<TEvent>(
        EventInboxOptions? options = null)
        where TEvent : Event;

    /// <summary>
    /// Create a caller-owned class-event channel inbox.
    /// </summary>
    EventInbox<Event> CreateChannelInbox(
        EventChannel channel,
        EventInboxOptions? options = null);

    /// <summary>
    /// Set parent coordinator for hierarchical event bubbling.
    /// Events emitted to this coordinator will automatically bubble to parent.
    /// No type conversions needed - all coordinators work with Event base class.
    /// </summary>
    /// <param name="parent">Parent coordinator to bubble events to</param>
    void SetParent(IEventCoordinator parent);

    /// <summary>
    /// Emit a bidirectional request event and wait for its matching response.
    /// The response waiter is registered before the request is emitted.
    /// </summary>
    Task<TResponse> RequestAsync<TRequest, TResponse>(
        TRequest request,
        TimeSpan timeout,
        CancellationToken ct = default)
        where TRequest : Event, IBidirectionalEvent
        where TResponse : Event;

    /// <summary>
    /// Complete a pending bidirectional request with a matching response.
    /// Throws when no active waiter exists.
    /// </summary>
    void Respond(string requestId, Event response);

    /// <summary>
    /// Try to complete a pending bidirectional request with a matching response.
    /// Returns false when the request has already completed, timed out, or never existed.
    /// </summary>
    bool TryRespond(string requestId, Event response);

    /// <summary>
    /// Registry for managing interruptible event flows.
    /// Allows grouping events into flows that can be interrupted together.
    /// </summary>
    IEventFlowRegistry EventFlows { get; }

    /// <summary>Returns current class-event bus health.</summary>
    EventCoordinatorStats GetStats();
}
