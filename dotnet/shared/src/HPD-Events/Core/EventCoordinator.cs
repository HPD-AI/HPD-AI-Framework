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
    private static readonly object GraphLock = new();
    private readonly HashSet<EventCoordinator> _forwardDestinations = [];
    private readonly HashSet<EventCoordinator> _childCoordinators = [];
    private readonly EventChannelRouter _events;

    /// <summary>
    /// Creates a new event coordinator with fan-out event-bus routing.
    /// </summary>
    public EventCoordinator(
        Func<Event, Event>? eventEnricher = null,
        Func<Event, bool>? eventFilter = null,
        EventOwnerId? ownerId = null)
    {
        _events = new EventChannelRouter(eventEnricher, eventFilter, ownerId);
    }

    internal IEventCoordinator? ParentCoordinatorForCycleDetection => _events.ParentCoordinator;

    internal void RegisterChildRouter(EventChannelRouter child) => _events.RegisterChild(child);

    internal void UnregisterChildRouter(EventChannelRouter child) => _events.UnregisterChild(child);

    /// <inheritdoc />
    public IEventFlowRegistry EventFlows => _events.EventFlows;

    public IReadOnlyList<PendingRequestSnapshot> GetPendingRequests()
        => _events.GetPendingRequests();

    /// <inheritdoc />
    public void Emit(Event evt) => _events.Emit(evt);

    /// <inheritdoc />
    public void Emit(Event evt, EventRouteDescriptor? route) => _events.Emit(evt, route);

    /// <inheritdoc />
    public ValueTask EmitAsync(Event evt, CancellationToken ct = default) =>
        _events.EmitAsync(evt, null, ct);

    /// <inheritdoc />
    public ValueTask EmitAsync(Event evt, EventRouteDescriptor? route, CancellationToken ct = default) =>
        _events.EmitAsync(evt, route, ct);

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
        if (parent is not EventCoordinator next)
            throw new NotSupportedException("Hierarchical routed delivery requires an EventCoordinator parent.");
        lock (GraphLock)
        {
            var previousParent = _events.ParentCoordinator;
            if (!ReferenceEquals(previousParent, parent))
            {
                if (CanReach(this, next))
                    throw new InvalidOperationException("The parent is already reachable from this coordinator.");
                if (CanReach(next, this))
                    throw new InvalidOperationException("Cannot set parent: this would create a cycle in the coordinator hierarchy.");
            }

            _events.SetParent(parent, this);
            if (previousParent is EventCoordinator previous && !ReferenceEquals(previous, next))
            {
                previous.UnregisterChildRouter(_events);
                previous._childCoordinators.Remove(this);
            }
            next.RegisterChildRouter(_events);
            next._childCoordinators.Add(this);
        }
    }

    /// <inheritdoc />
    public IEventCoordinator CreateChild(EventChildOwnership ownership)
    {
        var child = new EventCoordinator(ownerId: ownership == EventChildOwnership.InheritOwner ? _events.OwnerId : null);
        child.SetParent(this);
        return child;
    }

    /// <inheritdoc />
    public IDisposable ForwardTo(IEventCoordinator destination, EventForwardingOptions? options = null)
    {
        if (destination is not EventCoordinator target)
            throw new NotSupportedException("Provenance-preserving forwarding requires an EventCoordinator destination.");
        if (ReferenceEquals(this, target))
            throw new InvalidOperationException("A coordinator cannot forward to itself.");

        lock (GraphLock)
        {
            if (_forwardDestinations.Contains(target))
                throw new InvalidOperationException("This forwarding edge already exists.");
            if (SelfAndDescendants().Any(origin => CanReach(origin, target)))
                throw new InvalidOperationException("The destination is already reachable from this coordinator.");
            if (CanReach(target, this))
                throw new InvalidOperationException("The forwarding edge would create a coordinator cycle.");

            var bridge = _events.CreateForwardingBridge(target, options, RemoveForwardingEdge);
            _forwardDestinations.Add(target);
            return bridge;
        }

        void RemoveForwardingEdge(EventCoordinator removed)
        {
            lock (GraphLock)
                _forwardDestinations.Remove(removed);
        }
    }

    private IEnumerable<EventCoordinator> SelfAndDescendants()
    {
        var pending = new Stack<EventCoordinator>();
        var visited = new HashSet<EventCoordinator>();
        pending.Push(this);
        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current))
                continue;
            yield return current;
            foreach (var child in current._childCoordinators)
                pending.Push(child);
        }
    }

    private static bool CanReach(EventCoordinator source, EventCoordinator destination)
    {
        var pending = new Stack<EventCoordinator>();
        var visited = new HashSet<EventCoordinator>();
        pending.Push(source);
        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current))
                continue;
            if (!ReferenceEquals(current, source) && ReferenceEquals(current, destination))
                return true;
            if (current.ParentCoordinatorForCycleDetection is EventCoordinator parent)
                pending.Push(parent);
            foreach (var forwarded in current._forwardDestinations)
                pending.Push(forwarded);
        }
        return false;
    }

    internal void ReceiveFromChild(in RoutedEvent delivery) => _events.ReceiveFromChild(delivery);
    internal ValueTask ReceiveFromChildAsync(RoutedEvent delivery, CancellationToken ct) => _events.ReceiveFromChildAsync(delivery, ct);

    internal void ReceiveForwarded(in RoutedEvent delivery) => _events.ReceiveForwarded(delivery);

    internal DeliveryInbox<TDelivery> CreateProjectedDeliveryInbox<TEvent, TDelivery>(
        EventOwnerScope ownerScope,
        IEventDeliveryPolicy policy,
        IEventDeliveryProjector<TEvent, TDelivery> projector,
        EventInboxOptions? options = null)
        where TEvent : Event => _events.CreateProjectedDeliveryInbox(ownerScope, policy, projector, options);

    internal EventInbox<TEvent> CreateFilteredInbox<TEvent>(
        EventOwnerScope ownerScope,
        IEventDeliveryPolicy policy,
        EventInboxOptions? options = null)
        where TEvent : Event => _events.CreateFilteredInbox<TEvent>(ownerScope, policy, options);

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
        => StartRequest<TRequest, TResponse>(request, null, options);

    /// <inheritdoc />
    public RequestHandle StartRequest<TRequest, TResponse>(TRequest request, EventRouteDescriptor? route, RequestOptions? options = null)
        where TRequest : Event, IRequestEvent
        where TResponse : Event, IResponseEvent
    {
        ArgumentNullException.ThrowIfNull(request);
        return _events.StartRequest<TRequest, TResponse>(request, route, options);
    }

    public RequestHandle RegisterRequest<TRequest, TResponse>(
        TRequest request,
        RequestOptions? options = null)
        where TRequest : Event, IRequestEvent
        where TResponse : Event, IResponseEvent
        => RegisterRequest<TRequest, TResponse>(request, null, options);

    /// <inheritdoc />
    public RequestHandle RegisterRequest<TRequest, TResponse>(TRequest request, EventRouteDescriptor? route, RequestOptions? options = null)
        where TRequest : Event, IRequestEvent
        where TResponse : Event, IResponseEvent
    {
        ArgumentNullException.ThrowIfNull(request);
        return _events.RegisterRequest<TRequest, TResponse>(request, route, options);
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

    public async ValueTask<RespondResult> RespondAsync(
        string requestId,
        Event response,
        Func<Event, CancellationToken, ValueTask<Event>> beforeCompletion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(beforeCompletion);

        var parent = _events.ParentCoordinator as EventCoordinator;
        var result = await _events.RespondAsync(
            requestId,
            response,
            beforeCompletion,
            publishRejection: parent is null,
            cancellationToken).ConfigureAwait(false);
        if (result.Accepted)
            return result;

        return parent is not null && result.Status == RespondStatus.NotFound
            ? await parent.RespondAsync(
                requestId,
                response,
                beforeCompletion,
                cancellationToken).ConfigureAwait(false)
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
        lock (GraphLock)
        {
            if (_events.ParentCoordinator is EventCoordinator parent)
                parent._childCoordinators.Remove(this);
            _events.Dispose();
            _forwardDestinations.Clear();
            _childCoordinators.Clear();
        }
    }
}
