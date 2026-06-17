namespace HPD.Events.Core;

/// <summary>
/// Public event coordinator facade.
/// </summary>
public sealed class EventCoordinator :
    IEventCoordinator,
    IEventBus,
    IRequestResponseBus,
    IHierarchicalEventBus,
    IDisposable
{
    private readonly EventChannelRouter _events;

    /// <summary>
    /// Creates a new event coordinator with fan-out event-bus routing.
    /// </summary>
    public EventCoordinator(
        Func<Event, Event>? eventEnricher = null,
        Func<Event, bool>? eventFilter = null)
    {
        _events = new EventChannelRouter(eventEnricher, eventFilter);
    }

    internal IEventCoordinator? ParentCoordinatorForCycleDetection => _events.ParentCoordinator;

    internal void RegisterChildRouter(EventChannelRouter child) => _events.RegisterChild(child);

    internal void UnregisterChildRouter(EventChannelRouter child) => _events.UnregisterChild(child);

    /// <inheritdoc />
    public IEventFlowRegistry EventFlows => _events.EventFlows;

    /// <inheritdoc />
    public void Emit(Event evt) => _events.Emit(evt);

    /// <inheritdoc />
    public ValueTask EmitAsync(Event evt, CancellationToken ct = default) =>
        _events.EmitAsync(evt, ct);

    /// <inheritdoc />
    public IDisposable Subscribe<TEvent>(
        Func<TEvent, ValueTask> handler,
        EventSubscriptionOptions? options = null)
        where TEvent : Event =>
        _events.Subscribe(handler, options);

    /// <inheritdoc />
    public IDisposable SubscribeAny(
        Func<Event, ValueTask> handler,
        EventSubscriptionOptions? options = null) =>
        _events.SubscribeAny(handler, options);

    /// <inheritdoc />
    public EventInbox<TEvent> CreateInbox<TEvent>(
        EventInboxOptions? options = null)
        where TEvent : Event =>
        _events.CreateInbox<TEvent>(options);

    /// <inheritdoc />
    public EventInbox<Event> CreateChannelInbox(
        EventChannel channel,
        EventInboxOptions? options = null) =>
        _events.CreateChannelInbox(channel, options);

    /// <inheritdoc />
    public void SetParent(IEventCoordinator parent)
    {
        var previousParent = _events.ParentCoordinator;
        _events.SetParent(parent, this);

        if (previousParent is EventCoordinator previous)
            previous.UnregisterChildRouter(_events);

        if (parent is EventCoordinator next)
            next.RegisterChildRouter(_events);
    }

    void IHierarchicalEventBus.SetParent(IEventBus parent)
    {
        if (parent is not EventCoordinator coordinator)
        {
            throw new NotSupportedException(
                "Full event-bus hierarchy support requires an EventCoordinator parent.");
        }

        SetParent(coordinator);
    }

    /// <inheritdoc />
    public RequestHandle StartRequest<TRequest, TResponse>(
        TRequest request,
        RequestOptions? options = null)
        where TRequest : Event, IRequestEvent
        where TResponse : Event, IResponseEvent
    {
        ArgumentNullException.ThrowIfNull(request);
        return _events.StartRequest<TRequest, TResponse>(request, options);
    }

    /// <inheritdoc />
    public async Task<TResponse> RequestAsync<TRequest, TResponse>(
        TRequest request,
        TimeSpan timeout,
        CancellationToken ct = default)
        where TRequest : Event, IRequestEvent
        where TResponse : Event, IResponseEvent
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _events.RequestAsync<TRequest, TResponse>(request, timeout, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public RespondResult Respond(Event response)
    {
        if (response is not IResponseEvent responseEvent)
            throw new ArgumentException("Response event must implement IResponseEvent.", nameof(response));

        return Respond(responseEvent.RequestId, response);
    }

    /// <inheritdoc />
    public RespondResult Respond(string requestId, Event response)
    {
        var parent = _events.ParentCoordinator as EventCoordinator;
        var result = _events.Respond(requestId, response, publishRejection: parent is null);
        if (result.Accepted)
            return result;

        return parent is not null && result.Status == RespondStatus.NotFound
            ? parent.Respond(requestId, response)
            : result;
    }

    /// <inheritdoc />
    public EventCoordinatorStats GetStats() => _events.GetStats();

    EventBusStats IEventBus.GetStats() => _events.GetBusStats();

    /// <summary>
    /// Dispose coordinator and complete all class-event channels.
    /// </summary>
    public void Dispose()
    {
        _events.Dispose();
    }
}
