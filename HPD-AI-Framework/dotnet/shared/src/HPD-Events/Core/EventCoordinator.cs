namespace HPD.Events.Core;

/// <summary>
/// Public event coordinator facade.
/// </summary>
public sealed class EventCoordinator : IEventCoordinator, IDisposable
{
    private readonly EventChannelRouter _events;
    private readonly StructEventRouter _structs = new();

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
    public IStreamRegistry Streams => _events.Streams;

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
    public EventStreamSubscription<TEvent> SubscribeStream<TEvent>(
        EventSubscriptionOptions? options = null)
        where TEvent : Event =>
        _events.SubscribeStream<TEvent>(options);

    /// <inheritdoc />
    public EventStreamSubscription<Event> SubscribeChannel(
        EventChannel channel,
        EventSubscriptionOptions? options = null) =>
        _events.SubscribeChannel(channel, options);

    /// <inheritdoc />
    public bool TryEmitStruct<TEvent>(in TEvent evt) where TEvent : struct, IStructEvent =>
        _structs.TryEmitStruct(in evt);

    /// <inheritdoc />
    public ValueTask EmitStructAsync<TEvent>(TEvent evt, CancellationToken ct = default)
        where TEvent : struct, IStructEvent =>
        _structs.EmitStructAsync(evt, ct);

    /// <inheritdoc />
    public StructSubscription<TEvent> SubscribeStruct<TEvent>(StructSubscriptionOptions? options = null)
        where TEvent : struct, IStructEvent =>
        _structs.SubscribeStruct<TEvent>(options);

    /// <inheritdoc />
    public IDisposable SubscribeStruct<TEvent>(Func<TEvent, ValueTask> handler)
        where TEvent : struct, IStructEvent =>
        _structs.SubscribeStruct(handler);

    /// <inheritdoc />
    public StructEmitter<TEvent> CreateStructEmitter<TEvent>(StructEmitterOptions<TEvent>? options = null)
        where TEvent : struct, IStructEvent =>
        _structs.CreateStructEmitter(options);

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

    /// <inheritdoc />
    public async Task<TResponse> RequestAsync<TRequest, TResponse>(
        TRequest request,
        TimeSpan timeout,
        CancellationToken ct = default)
        where TRequest : Event, IBidirectionalEvent
        where TResponse : Event
    {
        ArgumentNullException.ThrowIfNull(request);

        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var responseTask = _events.WaitForResponseAsync<TResponse>(
            request.RequestId,
            timeout,
            requestCts.Token);

        try
        {
            Emit(request);
        }
        catch
        {
            await requestCts.CancelAsync().ConfigureAwait(false);
            throw;
        }

        return await responseTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Respond(string requestId, Event response)
    {
        if (!TryRespond(requestId, response))
            throw new InvalidOperationException($"No pending response waiter found for request ID '{requestId}'.");
    }

    /// <inheritdoc />
    public bool TryRespond(string requestId, Event response)
    {
        if (_events.TryRespond(requestId, response))
            return true;

        return _events.ParentCoordinator is EventCoordinator parent &&
            parent.TryRespond(requestId, response);
    }

    /// <inheritdoc />
    public EventCoordinatorStats GetStats() => _events.GetStats();

    /// <summary>
    /// Dispose coordinator and complete all class-event channels and struct subscriptions.
    /// </summary>
    public void Dispose()
    {
        _events.Dispose();
        _structs.Dispose();
    }
}
