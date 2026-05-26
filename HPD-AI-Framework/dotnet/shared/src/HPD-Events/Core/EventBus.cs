namespace HPD.Events.Core;

/// <summary>
/// Public event bus facade with explicit publishing, observer, inbox, request/response,
/// hierarchy, and struct-event surfaces.
/// </summary>
public sealed class EventBus :
    IEventBus,
    IRequestResponseBus,
    IHierarchicalEventBus,
    IDisposable
{
    private readonly EventCoordinator _coordinator;

    /// <summary>
    /// Creates a new event bus with fan-out class-event routing.
    /// </summary>
    public EventBus(
        Func<Event, Event>? eventEnricher = null,
        Func<Event, bool>? eventFilter = null)
    {
        _coordinator = new EventCoordinator(eventEnricher, eventFilter);
    }

    internal EventCoordinator Coordinator => _coordinator;

    /// <summary>Process-local high-throughput struct event lanes.</summary>
    public ILocalStructEventBus LocalStructs => _coordinator.LocalStructs;

    /// <inheritdoc />
    public IEventFlowRegistry EventFlows => _coordinator.EventFlows;

    /// <inheritdoc />
    public void Emit(Event evt) => _coordinator.Emit(evt);

    /// <inheritdoc />
    public ValueTask EmitAsync(Event evt, CancellationToken ct = default) =>
        _coordinator.EmitAsync(evt, ct);

    /// <inheritdoc />
    public IDisposable Subscribe<TEvent>(
        Func<TEvent, ValueTask> handler,
        EventSubscriptionOptions? options = null)
        where TEvent : Event =>
        _coordinator.Subscribe(handler, options);

    /// <inheritdoc />
    public IDisposable SubscribeAny(
        Func<Event, ValueTask> handler,
        EventSubscriptionOptions? options = null) =>
        _coordinator.SubscribeAny(handler, options);

    /// <inheritdoc />
    public EventInbox<TEvent> CreateInbox<TEvent>(
        EventInboxOptions? options = null)
        where TEvent : Event =>
        _coordinator.CreateInbox<TEvent>(options);

    /// <inheritdoc />
    public EventInbox<Event> CreateChannelInbox(
        EventChannel channel,
        EventInboxOptions? options = null) =>
        _coordinator.CreateChannelInbox(channel, options);

    /// <inheritdoc />
    public EventBusStats GetStats() => ((IEventBus)_coordinator).GetStats();

    /// <inheritdoc />
    public Task<TResponse> RequestAsync<TRequest, TResponse>(
        TRequest request,
        TimeSpan timeout,
        CancellationToken ct = default)
        where TRequest : Event, IBidirectionalEvent
        where TResponse : Event =>
        _coordinator.RequestAsync<TRequest, TResponse>(request, timeout, ct);

    /// <inheritdoc />
    public void Respond(string requestId, Event response) =>
        _coordinator.Respond(requestId, response);

    /// <inheritdoc />
    public bool TryRespond(string requestId, Event response) =>
        _coordinator.TryRespond(requestId, response);

    /// <inheritdoc />
    public void SetParent(IEventBus parent)
    {
        switch (parent)
        {
            case EventBus bus:
                _coordinator.SetParent(bus.Coordinator);
                break;
            case EventCoordinator coordinator:
                _coordinator.SetParent(coordinator);
                break;
            default:
                throw new NotSupportedException(
                    "Full event-bus hierarchy support requires an EventBus or EventCoordinator parent.");
        }
    }

    /// <inheritdoc />
    public void Dispose() => _coordinator.Dispose();
}
