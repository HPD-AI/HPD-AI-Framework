namespace HPD.Events.Core;

/// <summary>
/// Public event bus facade with explicit publishing, observer, inbox, request/response,
/// and hierarchy surfaces.
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

    /// <inheritdoc />
    public IEventFlowRegistry EventFlows => _coordinator.EventFlows;

    public IReadOnlyList<PendingRequestSnapshot> GetPendingRequests()
        => _coordinator.GetPendingRequests();

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
    public RequestHandle StartRequest<TRequest, TResponse>(
        TRequest request,
        RequestOptions? options = null)
        where TRequest : Event, IRequestEvent
        where TResponse : Event, IResponseEvent =>
        _coordinator.StartRequest<TRequest, TResponse>(request, options);

    public RequestHandle RegisterRequest<TRequest, TResponse>(
        TRequest request,
        RequestOptions? options = null)
        where TRequest : Event, IRequestEvent
        where TResponse : Event, IResponseEvent =>
        _coordinator.RegisterRequest<TRequest, TResponse>(request, options);

    /// <inheritdoc />
    public Task<TResponse> RequestAsync<TRequest, TResponse>(
        TRequest request,
        TimeSpan timeout,
        CancellationToken ct = default)
        where TRequest : Event, IRequestEvent
        where TResponse : Event, IResponseEvent =>
        _coordinator.RequestAsync<TRequest, TResponse>(request, timeout, ct);

    /// <inheritdoc />
    public RespondResult Respond(Event response) =>
        _coordinator.Respond(response);

    /// <inheritdoc />
    public RespondResult Respond(string requestId, Event response) =>
        _coordinator.Respond(requestId, response);

    public ValueTask<RespondResult> RespondAsync(
        string requestId,
        Event response,
        Func<Event, CancellationToken, ValueTask<Event>> beforeCompletion,
        CancellationToken cancellationToken = default) =>
        _coordinator.RespondAsync(requestId, response, beforeCompletion, cancellationToken);

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
