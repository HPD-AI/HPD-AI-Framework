namespace HPD.Events.Core;

/// <summary>
/// Public event coordinator facade.
/// </summary>
public sealed class EventCoordinator : IEventCoordinator, IDisposable
{
    private readonly EventChannelRouter _events;
    private readonly StructEventRouter _structs = new();

    /// <summary>
    /// Creates a new event coordinator with channel-based routing.
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
    public IEventCoordinator On<TEvent>(Func<TEvent, ValueTask> handler) where TEvent : Event
    {
        _events.On(handler);
        return this;
    }

    /// <inheritdoc />
    public IEventCoordinator OnAny(Func<Event, ValueTask> handler)
    {
        _events.OnAny(handler);
        return this;
    }

    /// <inheritdoc />
    public bool TryEmitStruct<TEvent>(in TEvent evt) where TEvent : struct, IStructEvent =>
        _structs.TryEmitStruct(in evt);

    /// <inheritdoc />
    public ValueTask EmitStructAsync<TEvent>(TEvent evt, CancellationToken ct = default)
        where TEvent : struct, IStructEvent =>
        _structs.EmitStructAsync(evt, ct);

    /// <inheritdoc />
    public IEventCoordinator OnStruct<TEvent>(Func<TEvent, ValueTask> handler)
        where TEvent : struct, IStructEvent
    {
        _structs.OnStruct(handler);
        return this;
    }

    /// <inheritdoc />
    public StructSubscription<TEvent> SubscribeStruct<TEvent>(StructSubscriptionOptions? options = null)
        where TEvent : struct, IStructEvent =>
        _structs.SubscribeStruct<TEvent>(options);

    /// <inheritdoc />
    public StructEmitter<TEvent> CreateStructEmitter<TEvent>(StructEmitterOptions<TEvent>? options = null)
        where TEvent : struct, IStructEvent =>
        _structs.CreateStructEmitter(options);

    /// <inheritdoc />
    public Task RunAsync(CancellationToken ct = default) =>
        Task.WhenAll(_events.RunAsync(ct), _structs.RunAsync(ct));

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
    public Task<TResponse> WaitForResponseAsync<TResponse>(
        string requestId,
        TimeSpan timeout,
        CancellationToken ct = default) where TResponse : Event =>
        _events.WaitForResponseAsync<TResponse>(requestId, timeout, ct);

    /// <inheritdoc />
    public void SendResponse(string requestId, Event response)
    {
        if (_events.SendResponse(requestId, response))
            return;

        if (_events.ParentCoordinator is EventCoordinator parent)
            parent.SendResponse(requestId, response);
    }

    /// <inheritdoc />
    public EventCoordinatorStats GetStats() => _events.GetStats();

    /// <inheritdoc />
    public IAsyncEnumerable<Event> ReadStreamingAsync(CancellationToken ct = default) =>
        _events.ReadStreamingAsync(ct);

    /// <inheritdoc />
    public IAsyncEnumerable<Event> ReadSynchronousAsync(CancellationToken ct = default) =>
        _events.ReadSynchronousAsync(ct);

    /// <inheritdoc />
    public IAsyncEnumerable<Event> ReadInteractiveAsync(CancellationToken ct = default) =>
        _events.ReadInteractiveAsync(ct);

    /// <inheritdoc />
    public IAsyncEnumerable<Event> ReadControlAsync(CancellationToken ct = default) =>
        _events.ReadControlAsync(ct);

    /// <summary>
    /// Dispose coordinator and complete all class-event channels and struct subscriptions.
    /// </summary>
    public void Dispose()
    {
        _events.Dispose();
        _structs.Dispose();
    }
}
