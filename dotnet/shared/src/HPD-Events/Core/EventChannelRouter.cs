using System.Collections.Concurrent;
using System.Threading.Channels;

namespace HPD.Events.Core;

/// <summary>
/// Routes semantic HPD events through per-subscriber fan-out mailboxes.
/// </summary>
internal sealed class EventChannelRouter : IDisposable
{
    private readonly ConcurrentDictionary<string, RequestSession> _requestSessions = new();
    private readonly ConcurrentDictionary<string, TerminalRequestState> _terminalRequests = new();
    private readonly ConcurrentDictionary<EventChannelRouter, byte> _children = new();
    private readonly List<IClassEventSubscriber> _subscribers = new();
    private readonly EventFlowRegistry _eventFlowRegistry = new();
    private readonly Func<Event, Event>? _eventEnricher;
    private readonly Func<Event, bool>? _eventFilter;

    private long _sequenceCounter;
    private long _totalDropped;
    private IEventCoordinator? _parentCoordinator;
    private bool _disposed;

    public EventChannelRouter(
        Func<Event, Event>? eventEnricher = null,
        Func<Event, bool>? eventFilter = null)
    {
        _eventEnricher = eventEnricher;
        _eventFilter = eventFilter;
    }

    public IEventFlowRegistry EventFlows => _eventFlowRegistry;

    internal IEventCoordinator? ParentCoordinator => _parentCoordinator;

    internal void RegisterChild(EventChannelRouter child)
    {
        ArgumentNullException.ThrowIfNull(child);
        ObjectDisposedException.ThrowIf(_disposed, this);

        _children[child] = 0;
    }

    internal void UnregisterChild(EventChannelRouter child)
    {
        ArgumentNullException.ThrowIfNull(child);
        _children.TryRemove(child, out _);
    }

    public void Emit(Event evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var enriched = PrepareForEmission(evt);
        if (enriched is null)
            return;

        PublishPrepared(enriched, skipSubscriberId: null);
        BubblePrepared(enriched);
    }

    public async ValueTask EmitAsync(Event evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var enriched = PrepareForEmission(evt);
        if (enriched is null)
            return;

        await PublishPreparedAsync(enriched, skipSubscriberId: null, ct).ConfigureAwait(false);
        await BubblePreparedAsync(enriched, ct).ConfigureAwait(false);
    }

    public IDisposable Subscribe<TEvent>(
        Func<TEvent, ValueTask> handler,
        EventSubscriptionOptions? options = null)
        where TEvent : Event
    {
        ArgumentNullException.ThrowIfNull(handler);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var subscriber = CreateSubscriber<TEvent>(isInbox: false, options);
        var cts = new CancellationTokenSource();
        var task = Task.Run(
            () => RunHandlerPumpAsync(subscriber, handler, cts.Token),
            CancellationToken.None);

        return new HandlerSubscription<TEvent>(this, subscriber, cts, task);
    }

    public IDisposable SubscribeAny(
        Func<Event, ValueTask> handler,
        EventSubscriptionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return Subscribe<Event>(handler, options);
    }

    public EventInbox<TEvent> CreateInbox<TEvent>(
        EventInboxOptions? options = null)
        where TEvent : Event
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        options ??= new EventInboxOptions();
        var subscriber = CreateSubscriber<TEvent>(
            isInbox: true,
            options.ToSubscriptionOptions());

        return new EventInbox<TEvent>(
            subscriber.Reader,
            subscriber.Writer,
            writer => RemoveSubscriberByWriter(writer));
    }

    public EventInbox<Event> CreateChannelInbox(
        EventChannel channel,
        EventInboxOptions? options = null)
    {
        options = (options ?? new EventInboxOptions()) with { Channel = channel };
        return CreateInbox<Event>(options);
    }

    public void SetParent(IEventCoordinator parent, IEventCoordinator owner)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (parent == owner)
        {
            throw new InvalidOperationException(
                "Cannot set coordinator as its own parent. This would create an infinite loop during event emission.");
        }

        var current = parent;
        while (current is not null)
        {
            if (current == owner)
            {
                throw new InvalidOperationException(
                    "Cannot set parent: this would create a cycle in the coordinator hierarchy, " +
                    "causing infinite loops during event emission.");
            }

            if (current is EventCoordinator coordinator)
            {
                current = coordinator.ParentCoordinatorForCycleDetection;
            }
            else
            {
                break;
            }
        }

        _parentCoordinator = parent;
    }

    public RequestHandle StartRequest<TRequest, TResponse>(
        TRequest request,
        RequestOptions? options = null)
        where TRequest : Event, IRequestEvent
        where TResponse : Event, IResponseEvent
    {
        var handle = RegisterRequest<TRequest, TResponse>(request, options);
        try
        {
            Emit(request);
            return handle;
        }
        catch
        {
            handle.Cancel("Request publication failed.");
            throw;
        }
    }

    public RequestHandle RegisterRequest<TRequest, TResponse>(
        TRequest request,
        RequestOptions? options = null)
        where TRequest : Event, IRequestEvent
        where TResponse : Event, IResponseEvent
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(request.RequestId))
            throw new ArgumentException("Request ID cannot be null or whitespace", nameof(request));

        options ??= new RequestOptions();
        var timeoutCts = options.CancellationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(options.CancellationToken)
            : new CancellationTokenSource();
        var completion = new TaskCompletionSource<Event>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new RequestSession(
            request.RequestId,
            request.SourceName,
            request,
            typeof(TRequest),
            typeof(TResponse),
            completion,
            timeoutCts,
            options.Timeout,
            request.ResponsePolicy,
            request.Target,
            request.Visibility);

        if (!_requestSessions.TryAdd(request.RequestId, session))
        {
            timeoutCts.Dispose();
            throw new InvalidOperationException($"Duplicate request ID: {request.RequestId}");
        }

        if (options.Timeout is { } timeout)
        {
            timeoutCts.CancelAfter(timeout);
        }

        session.CancellationRegistration = timeoutCts.Token.Register(() =>
        {
            if (options.CancellationToken.IsCancellationRequested)
                CancelLocalRequest(request.RequestId, "Request cancellation was requested.");
            else
                ExpireLocalRequest(request.RequestId);
        });

        PublishDiagnostic(new RequestStartedEvent(
            request.RequestId,
            request.SourceName,
            typeof(TRequest).Name,
            typeof(TResponse).Name,
            request.ResponsePolicy,
            request.Target,
            request.Visibility,
            session.CreatedAt), skipSubscriberId: null);

        return new RequestHandle(
            request.RequestId,
            completion.Task,
            () => session.ToSnapshot(),
            reason => CancelLocalRequest(request.RequestId, reason));
    }

    public IReadOnlyList<PendingRequestSnapshot> GetPendingRequests()
    {
        var pending = new List<PendingRequestSnapshot>();
        CollectPendingRequests(pending);
        return pending
            .OrderBy(item => item.Session.CreatedAt)
            .ToArray();
    }

    private void CollectPendingRequests(List<PendingRequestSnapshot> pending)
    {
        foreach (var session in _requestSessions.Values)
        {
            var snapshot = session.ToSnapshot();
            if (snapshot.State == RequestState.Pending)
            {
                pending.Add(new PendingRequestSnapshot(session.Request, snapshot));
            }
        }

        foreach (var child in _children.Keys)
        {
            child.CollectPendingRequests(pending);
        }
    }

    internal async Task<TResponse> RequestAsync<TRequest, TResponse>(
        TRequest request,
        TimeSpan timeout,
        CancellationToken ct = default)
        where TRequest : Event, IRequestEvent
        where TResponse : Event, IResponseEvent
    {
        var handle = StartRequest<TRequest, TResponse>(
            request,
            new RequestOptions { Timeout = timeout, CancellationToken = ct });

        try
        {
            var response = await handle.Response.ConfigureAwait(false);
            return (TResponse)response;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"No response received for request ID '{request.RequestId}' within {timeout.TotalSeconds:F1}s");
        }
    }

    public RespondResult Respond(Event response)
    {
        if (response is not IResponseEvent responseEvent)
        {
            throw new ArgumentException("Response event must implement IResponseEvent.", nameof(response));
        }

        return Respond(responseEvent.RequestId, response);
    }

    public RespondResult Respond(string requestId, Event response, bool publishRejection = true)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            throw new ArgumentException("Request ID cannot be null or whitespace", nameof(requestId));

        ArgumentNullException.ThrowIfNull(response);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var matches = new List<EventChannelRouter>();
        FindRequestSessions(requestId, matches);

        if (matches.Count == 0)
        {
            var terminal = FindTerminalRequest(requestId);
            var status = terminal?.Status switch
            {
                RespondStatus.Accepted => RespondStatus.AlreadyResolved,
                RespondStatus.TimedOut => RespondStatus.TimedOut,
                RespondStatus.Cancelled => RespondStatus.Cancelled,
                _ => RespondStatus.NotFound
            };
            var result = RespondResult.For(status, requestId, terminal?.Message ?? "No pending request session found.");
            if (publishRejection)
                PublishResponseRejected(requestId, response, result);

            return result;
        }

        if (matches.Count > 1)
        {
            var result = RespondResult.For(
                RespondStatus.AmbiguousRequest,
                requestId,
                $"Multiple pending request sessions found for request ID '{requestId}' in the coordinator hierarchy.");
            PublishResponseRejected(requestId, response, result);
            return result;
        }

        return matches[0].CompleteLocalResponse(requestId, response);
    }

    public async ValueTask<RespondResult> RespondAsync(
        string requestId,
        Event response,
        Func<Event, CancellationToken, ValueTask<Event>> beforeCompletion,
        bool publishRejection = true,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            throw new ArgumentException("Request ID cannot be null or whitespace", nameof(requestId));

        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(beforeCompletion);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var matches = new List<EventChannelRouter>();
        FindRequestSessions(requestId, matches);
        if (matches.Count == 0)
        {
            var terminal = FindTerminalRequest(requestId);
            var status = terminal?.Status switch
            {
                RespondStatus.Accepted => RespondStatus.AlreadyResolved,
                RespondStatus.TimedOut => RespondStatus.TimedOut,
                RespondStatus.Cancelled => RespondStatus.Cancelled,
                _ => RespondStatus.NotFound
            };
            var result = RespondResult.For(status, requestId, terminal?.Message ?? "No pending request session found.");
            if (publishRejection)
                PublishResponseRejected(requestId, response, result);
            return result;
        }

        if (matches.Count > 1)
        {
            var result = RespondResult.For(
                RespondStatus.AmbiguousRequest,
                requestId,
                $"Multiple pending request sessions found for request ID '{requestId}' in the coordinator hierarchy.");
            PublishResponseRejected(requestId, response, result);
            return result;
        }

        return await matches[0].CompleteLocalResponseAsync(
            requestId,
            response,
            beforeCompletion,
            cancellationToken).ConfigureAwait(false);
    }

    public EventCoordinatorStats GetStats()
    {
        var stats = GetBusStats();
        return new EventCoordinatorStats(
            stats.SubscriberCount,
            stats.InboxCount,
            stats.TotalQueued,
            stats.TotalDropped,
            stats.MaxSubscriberDepth);
    }

    public EventBusStats GetBusStats()
    {
        IClassEventSubscriber[] subscribers;
        lock (_subscribers)
        {
            subscribers = _subscribers.ToArray();
        }

        var totalQueued = 0;
        var maxDepth = 0;
        var inboxes = 0;
        foreach (var subscriber in subscribers)
        {
            var depth = subscriber.Depth;
            totalQueued += depth;
            maxDepth = Math.Max(maxDepth, depth);
            if (subscriber.IsInbox)
                inboxes++;
        }

        return new EventBusStats(
            subscribers.Length,
            inboxes,
            totalQueued,
            (int)Math.Min(int.MaxValue, Volatile.Read(ref _totalDropped)),
            maxDepth);
    }

    private EventSubscriber<TEvent> CreateSubscriber<TEvent>(
        bool isInbox,
        EventSubscriptionOptions? options)
        where TEvent : Event
    {
        options ??= new EventSubscriptionOptions();
        if (options.Capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Event subscription capacity must be greater than zero.");

        var subscriber = new EventSubscriber<TEvent>(options, isInbox, OnSubscriberDropped);
        lock (_subscribers)
        {
            _subscribers.Add(subscriber);
        }

        return subscriber;
    }

    private Event? PrepareForEmission(Event evt)
    {
        if (_eventFilter != null && !_eventFilter(evt))
            return null;

        var enriched = _eventEnricher?.Invoke(evt) ?? evt;
        var deliveryOrdinal = Interlocked.Increment(ref _sequenceCounter);

        if (enriched.EventFlowId is not null && enriched.CanInterrupt && enriched is not EventDroppedEvent)
        {
            var eventFlowHandle = _eventFlowRegistry.Get(enriched.EventFlowId);
            if (eventFlowHandle is EventFlowHandle { IsInterrupted: true } handle)
            {
                handle.IncrementDroppedCount();
                PublishDiagnostic(new EventDroppedEvent(
                    enriched.EventFlowId,
                    enriched.GetType().Name,
                    deliveryOrdinal),
                    skipSubscriberId: null);
                return null;
            }
        }

        if (enriched.EventFlowId is not null && enriched.CanInterrupt && enriched is not EventDroppedEvent)
        {
            if (_eventFlowRegistry.Get(enriched.EventFlowId) is EventFlowHandle handle)
                handle.IncrementEmittedCount();
        }

        return enriched;
    }

    private void PublishPrepared(Event evt, Guid? skipSubscriberId)
    {
        var subscribers = SnapshotSubscribers();
        foreach (var subscriber in subscribers)
        {
            if (skipSubscriberId == subscriber.Id)
                continue;

            if (!subscriber.Matches(evt))
                continue;

            if (!subscriber.TryPublish(evt))
                OnSubscriberDropped();
        }
    }

    private async ValueTask PublishPreparedAsync(Event evt, Guid? skipSubscriberId, CancellationToken ct)
    {
        var subscribers = SnapshotSubscribers();
        foreach (var subscriber in subscribers)
        {
            if (skipSubscriberId == subscriber.Id)
                continue;

            if (!subscriber.Matches(evt))
                continue;

            if (subscriber.TryPublish(evt))
                continue;

            if (subscriber.Options.FullMode == BoundedChannelFullMode.Wait)
            {
                await subscriber.PublishAsync(evt, ct).ConfigureAwait(false);
            }
            else
            {
                OnSubscriberDropped();
            }
        }
    }

    private void PublishDiagnostic(Event diagnostic, Guid? skipSubscriberId)
    {
        Interlocked.Increment(ref _sequenceCounter);

        PublishPrepared(diagnostic, skipSubscriberId);
        BubblePrepared(diagnostic);
    }

    private void BubblePrepared(Event evt)
    {
        _parentCoordinator?.Emit(evt);
    }

    private async ValueTask BubblePreparedAsync(Event evt, CancellationToken ct)
    {
        if (_parentCoordinator is not null)
            await _parentCoordinator.EmitAsync(evt, ct).ConfigureAwait(false);
    }

    private IClassEventSubscriber[] SnapshotSubscribers()
    {
        lock (_subscribers)
        {
            return _subscribers.ToArray();
        }
    }

    private void RemoveSubscriber(Guid subscriberId)
    {
        lock (_subscribers)
        {
            var index = _subscribers.FindIndex(subscriber => subscriber.Id == subscriberId);
            if (index < 0)
                return;

            _subscribers[index].Complete();
            _subscribers.RemoveAt(index);
        }
    }

    private void RemoveSubscriberByWriter<TEvent>(ChannelWriter<TEvent> writer)
        where TEvent : Event
    {
        lock (_subscribers)
        {
            var index = _subscribers.FindIndex(subscriber => subscriber.IsWriter(writer));
            if (index < 0)
                return;

            _subscribers[index].Complete();
            _subscribers.RemoveAt(index);
        }
    }

    private void OnSubscriberDropped() => Interlocked.Increment(ref _totalDropped);

    private void FindRequestSessions(string requestId, List<EventChannelRouter> matches)
    {
        if (_disposed)
            return;

        if (_requestSessions.ContainsKey(requestId))
            matches.Add(this);

        foreach (var child in _children.Keys)
            child.FindRequestSessions(requestId, matches);
    }

    private TerminalRequestState? FindTerminalRequest(string requestId)
    {
        if (_terminalRequests.TryGetValue(requestId, out var terminal))
            return terminal;

        foreach (var child in _children.Keys)
        {
            if (child.FindTerminalRequest(requestId) is { } childTerminal)
                return childTerminal;
        }

        return null;
    }

    private RespondResult CompleteLocalResponse(string requestId, Event response)
    {
        if (!_requestSessions.TryGetValue(requestId, out var session))
            return RespondResult.For(RespondStatus.NotFound, requestId, "No pending request session found.");

        if (response is not IResponseEvent responseEvent)
        {
            var result = RespondResult.For(
                RespondStatus.ResponseTypeMismatch,
                requestId,
                "Response event must implement IResponseEvent.");
            PublishResponseRejected(requestId, response, result);
            return result;
        }

        if (!session.ExpectedResponseType.IsInstanceOfType(response))
        {
            var result = RespondResult.For(
                RespondStatus.ResponseTypeMismatch,
                requestId,
                $"Response type mismatch. Expected {session.ExpectedResponseType.Name}, got {response.GetType().Name}.");
            PublishResponseRejected(requestId, response, result);
            return result;
        }

        if (!MatchesTarget(session, responseEvent))
        {
            var result = RespondResult.For(
                RespondStatus.TargetMismatch,
                requestId,
                "Response did not match the request responder target.");
            PublishResponseRejected(requestId, response, result);
            return result;
        }

        if (!session.TryBeginResolution())
        {
            var result = RespondResult.For(RespondStatus.AlreadyResolved, requestId, "Request has already resolved.");
            PublishResponseRejected(requestId, response, result);
            return result;
        }

        var resolvedAt = DateTimeOffset.UtcNow;
        _requestSessions.TryRemove(requestId, out _);
        session.MarkResolved(resolvedAt);
        session.DisposeCancellation();
        _terminalRequests[requestId] = new TerminalRequestState(RespondStatus.Accepted, "Request has already resolved.");
        session.Completion.TrySetResult(response);

        PublishDiagnostic(new RequestResolvedEvent(
            requestId,
            session.SourceName,
            session.RequestType.Name,
            response.GetType().Name,
            responseEvent.ResponderId,
            responseEvent.ResponderGroup,
            resolvedAt), skipSubscriberId: null);

        return RespondResult.For(RespondStatus.Accepted, requestId);
    }

    private async ValueTask<RespondResult> CompleteLocalResponseAsync(
        string requestId,
        Event response,
        Func<Event, CancellationToken, ValueTask<Event>> beforeCompletion,
        CancellationToken cancellationToken)
    {
        if (!_requestSessions.TryGetValue(requestId, out var session))
            return RespondResult.For(RespondStatus.NotFound, requestId, "No pending request session found.");

        if (response is not IResponseEvent responseEvent)
        {
            var result = RespondResult.For(
                RespondStatus.ResponseTypeMismatch,
                requestId,
                "Response event must implement IResponseEvent.");
            PublishResponseRejected(requestId, response, result);
            return result;
        }

        if (!session.ExpectedResponseType.IsInstanceOfType(response))
        {
            var result = RespondResult.For(
                RespondStatus.ResponseTypeMismatch,
                requestId,
                $"Response type mismatch. Expected {session.ExpectedResponseType.Name}, got {response.GetType().Name}.");
            PublishResponseRejected(requestId, response, result);
            return result;
        }

        if (!MatchesTarget(session, responseEvent))
        {
            var result = RespondResult.For(
                RespondStatus.TargetMismatch,
                requestId,
                "Response did not match the request responder target.");
            PublishResponseRejected(requestId, response, result);
            return result;
        }

        if (!session.TryBeginResolution())
        {
            var result = RespondResult.For(RespondStatus.AlreadyResolved, requestId, "Request has already resolved.");
            PublishResponseRejected(requestId, response, result);
            return result;
        }

        Event completedResponse;
        try
        {
            completedResponse = await beforeCompletion(response, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            session.RevertResolution();
            throw;
        }

        if (completedResponse.GetType() != response.GetType() ||
            completedResponse is not IResponseEvent completedResponseEvent ||
            !StringComparer.Ordinal.Equals(completedResponseEvent.RequestId, requestId))
        {
            session.RevertResolution();
            throw new InvalidOperationException(
                "The response completion boundary must return the same response type and request identity.");
        }

        var resolvedAt = DateTimeOffset.UtcNow;
        _requestSessions.TryRemove(requestId, out _);
        session.MarkResolved(resolvedAt);
        session.DisposeCancellation();
        _terminalRequests[requestId] = new TerminalRequestState(RespondStatus.Accepted, "Request has already resolved.");
        session.Completion.TrySetResult(completedResponse);

        PublishDiagnostic(new RequestResolvedEvent(
            requestId,
            session.SourceName,
            session.RequestType.Name,
            completedResponse.GetType().Name,
            completedResponseEvent.ResponderId,
            completedResponseEvent.ResponderGroup,
            resolvedAt), skipSubscriberId: null);

        return RespondResult.For(RespondStatus.Accepted, requestId);
    }

    private CancelRequestResult CancelLocalRequest(string requestId, string? reason)
    {
        if (!_requestSessions.TryGetValue(requestId, out var session))
        {
            if (_terminalRequests.TryGetValue(requestId, out var terminal))
            {
                return terminal.Status switch
                {
                    RespondStatus.Accepted => new CancelRequestResult(CancelRequestStatus.AlreadyResolved, requestId),
                    RespondStatus.TimedOut => new CancelRequestResult(CancelRequestStatus.TimedOut, requestId),
                    RespondStatus.Cancelled => new CancelRequestResult(CancelRequestStatus.AlreadyCancelled, requestId),
                    _ => new CancelRequestResult(CancelRequestStatus.NotFound, requestId)
                };
            }

            return new CancelRequestResult(CancelRequestStatus.NotFound, requestId);
        }

        var cancelledAt = DateTimeOffset.UtcNow;
        if (!session.TryMarkCancelled(cancelledAt))
            return new CancelRequestResult(CancelRequestStatus.AlreadyResolved, requestId);

        _requestSessions.TryRemove(requestId, out _);
        session.DisposeCancellation();
        _terminalRequests[requestId] = new TerminalRequestState(RespondStatus.Cancelled, reason ?? "Request was cancelled.");
        session.Completion.TrySetCanceled();
        PublishDiagnostic(new RequestCancelledEvent(
            requestId,
            session.SourceName,
            session.RequestType.Name,
            reason,
            cancelledAt), skipSubscriberId: null);

        return new CancelRequestResult(CancelRequestStatus.Cancelled, requestId);
    }

    private void ExpireLocalRequest(string requestId)
    {
        if (!_requestSessions.TryGetValue(requestId, out var session))
            return;

        var expiredAt = DateTimeOffset.UtcNow;
        if (!session.TryMarkExpired(expiredAt))
            return;

        _requestSessions.TryRemove(requestId, out _);
        session.DisposeCancellation();
        _terminalRequests[requestId] = new TerminalRequestState(RespondStatus.TimedOut, "Request timed out.");
        session.Completion.TrySetCanceled();
        PublishDiagnostic(new RequestExpiredEvent(
            requestId,
            session.SourceName,
            session.RequestType.Name,
            session.Timeout ?? TimeSpan.Zero,
            expiredAt), skipSubscriberId: null);
    }

    private static bool MatchesTarget(RequestSession session, IResponseEvent response)
    {
        if (session.ResponsePolicy != ResponsePolicy.TargetedResponder || session.Target is not { } target)
            return true;

        if (!string.IsNullOrWhiteSpace(target.ResponderId) &&
            !string.Equals(target.ResponderId, response.ResponderId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(target.ResponderGroup) &&
            !string.Equals(target.ResponderGroup, response.ResponderGroup, StringComparison.Ordinal))
        {
            return false;
        }

        return target.RequiredCapabilities.Count == 0 ||
            target.RequiredCapabilities.All(response.Capabilities.Contains);
    }

    private void PublishResponseRejected(string requestId, Event response, RespondResult result)
    {
        var responseEvent = response as IResponseEvent;
        PublishDiagnostic(new ResponseRejectedEvent(
            requestId,
            response.GetType().Name,
            result.Status,
            result.Message,
            responseEvent?.ResponderId,
            responseEvent?.ResponderGroup,
            DateTimeOffset.UtcNow), skipSubscriberId: null);
    }

    private async Task RunHandlerPumpAsync<TEvent>(
        EventSubscriber<TEvent> subscriber,
        Func<TEvent, ValueTask> handler,
        CancellationToken ct)
        where TEvent : Event
    {
        try
        {
            await foreach (var evt in subscriber.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                subscriber.DecrementDepth();
                await handler(evt).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cooperative shutdown.
        }
        catch (Exception ex)
        {
            RemoveSubscriber(subscriber.Id);
            PublishDiagnostic(
                new EventSubscriberFaultedEvent(
                    subscriber.Id.ToString("N"),
                    typeof(TEvent).Name,
                    ex.GetType().Name,
                    ex.Message),
                skipSubscriberId: subscriber.Id);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_parentCoordinator is EventCoordinator parent)
            parent.UnregisterChildRouter(this);

        lock (_subscribers)
        {
            foreach (var subscriber in _subscribers)
                subscriber.Complete();

            _subscribers.Clear();
        }

        foreach (var (requestId, session) in _requestSessions)
        {
            if (!session.TryMarkCancelled(DateTimeOffset.UtcNow) ||
                !_requestSessions.TryRemove(requestId, out _))
            {
                // A reserved response may already be crossing its durable completion
                // boundary. It owns terminal cleanup and must not be cancelled after
                // commit has potentially begun.
                continue;
            }

            session.DisposeCancellation();
            _terminalRequests[requestId] = new TerminalRequestState(RespondStatus.Cancelled, "Coordinator disposed.");
            session.Completion.TrySetCanceled();
        }

        _children.Clear();
    }

    private sealed class RequestSession
    {
        private const int ResolvingState = 4;
        private int _state = (int)RequestState.Pending;
        private DateTimeOffset? _resolvedAt;

        public RequestSession(
            string requestId,
            string sourceName,
            Event request,
            Type requestType,
            Type expectedResponseType,
            TaskCompletionSource<Event> completion,
            CancellationTokenSource cancellation,
            TimeSpan? timeout,
            ResponsePolicy responsePolicy,
            ResponderTarget? target,
            RequestVisibility visibility)
        {
            RequestId = requestId;
            SourceName = sourceName;
            Request = request;
            RequestType = requestType;
            ExpectedResponseType = expectedResponseType;
            Completion = completion;
            Cancellation = cancellation;
            Timeout = timeout;
            ResponsePolicy = responsePolicy;
            Target = target;
            Visibility = visibility;
            CreatedAt = DateTimeOffset.UtcNow;
        }

        public string RequestId { get; }
        public string SourceName { get; }
        public Event Request { get; }
        public Type RequestType { get; }
        public Type ExpectedResponseType { get; }
        public TaskCompletionSource<Event> Completion { get; }
        public CancellationTokenSource Cancellation { get; }
        public TimeSpan? Timeout { get; }
        public ResponsePolicy ResponsePolicy { get; }
        public ResponderTarget? Target { get; }
        public RequestVisibility Visibility { get; }
        public DateTimeOffset CreatedAt { get; }
        public CancellationTokenRegistration CancellationRegistration { get; set; }

        public bool TryBeginResolution() =>
            Interlocked.CompareExchange(
                ref _state,
                ResolvingState,
                (int)RequestState.Pending) == (int)RequestState.Pending;

        public void RevertResolution()
        {
            if (Interlocked.CompareExchange(
                    ref _state,
                    (int)RequestState.Pending,
                    ResolvingState) != ResolvingState)
            {
                throw new InvalidOperationException("Request resolution reservation was lost.");
            }
        }

        public void MarkResolved(DateTimeOffset resolvedAt)
        {
            if (Interlocked.CompareExchange(
                    ref _state,
                    (int)RequestState.Resolved,
                    ResolvingState) != ResolvingState)
            {
                throw new InvalidOperationException("Request must be reserved before it can resolve.");
            }
            _resolvedAt = resolvedAt;
        }

        public bool TryMarkExpired(DateTimeOffset expiredAt)
        {
            if (Interlocked.CompareExchange(
                    ref _state,
                    (int)RequestState.Expired,
                    (int)RequestState.Pending) != (int)RequestState.Pending)
                return false;
            _resolvedAt = expiredAt;
            return true;
        }

        public bool TryMarkCancelled(DateTimeOffset cancelledAt)
        {
            if (Interlocked.CompareExchange(
                    ref _state,
                    (int)RequestState.Cancelled,
                    (int)RequestState.Pending) != (int)RequestState.Pending)
                return false;
            _resolvedAt = cancelledAt;
            return true;
        }

        public RequestSnapshot ToSnapshot() => new(
            RequestId,
            SourceName,
            RequestType.Name,
            ExpectedResponseType.Name,
            ResponsePolicy,
            Target,
            Visibility,
            Volatile.Read(ref _state) == ResolvingState
                ? RequestState.Pending
                : (RequestState)Volatile.Read(ref _state),
            CreatedAt,
            _resolvedAt);

        public void DisposeCancellation()
        {
            CancellationRegistration.Dispose();
            Cancellation.Dispose();
        }
    }

    private sealed record TerminalRequestState(RespondStatus Status, string? Message);

    private interface IClassEventSubscriber
    {
        Guid Id { get; }
        EventSubscriptionOptions Options { get; }
        bool IsInbox { get; }
        int Depth { get; }
        bool Matches(Event evt);
        bool TryPublish(Event evt);
        ValueTask PublishAsync(Event evt, CancellationToken ct);
        bool IsWriter<TEvent>(ChannelWriter<TEvent> writer) where TEvent : Event;
        void Complete();
    }

    private sealed class EventSubscriber<TEvent> : IClassEventSubscriber
        where TEvent : Event
    {
        private readonly Channel<TEvent> _channel;
        private readonly Action _onDropped;
        private int _depth;

        public EventSubscriber(
            EventSubscriptionOptions options,
            bool isInbox,
            Action onDropped)
        {
            Options = options;
            IsInbox = isInbox;
            _onDropped = onDropped;
            _channel = Channel.CreateBounded<TEvent>(
                new BoundedChannelOptions(options.Capacity)
                {
                    SingleReader = true,
                    SingleWriter = false,
                    FullMode = options.FullMode,
                    AllowSynchronousContinuations = false
                },
                itemDropped: _ =>
                {
                    DecrementDepth();
                    _onDropped();
                });
        }

        public Guid Id { get; } = Guid.NewGuid();
        public EventSubscriptionOptions Options { get; }
        public bool IsInbox { get; }
        public int Depth => Volatile.Read(ref _depth);
        public ChannelReader<TEvent> Reader => _channel.Reader;
        public ChannelWriter<TEvent> Writer => _channel.Writer;

        public bool Matches(Event evt)
        {
            if (Options.Channel is { } channel && evt.Channel != channel)
                return false;

            if (Options.IncludeDerivedTypes)
                return evt is TEvent;

            return evt.GetType() == typeof(TEvent);
        }

        public bool TryPublish(Event evt)
        {
            if (evt is not TEvent typed)
                return false;

            var written = _channel.Writer.TryWrite(typed);
            if (written)
                Interlocked.Increment(ref _depth);

            return written;
        }

        public async ValueTask PublishAsync(Event evt, CancellationToken ct)
        {
            if (evt is not TEvent typed)
                return;

            await _channel.Writer.WriteAsync(typed, ct).ConfigureAwait(false);
            Interlocked.Increment(ref _depth);
        }

        public void DecrementDepth()
        {
            int current;
            do
            {
                current = Volatile.Read(ref _depth);
                if (current <= 0)
                    return;
            }
            while (Interlocked.CompareExchange(ref _depth, current - 1, current) != current);
        }

        public bool IsWriter<T>(ChannelWriter<T> writer)
            where T : Event =>
            ReferenceEquals(_channel.Writer, writer);

        public void Complete() => _channel.Writer.TryComplete();
    }

    private sealed class HandlerSubscription<TEvent> : IDisposable
        where TEvent : Event
    {
        private readonly EventChannelRouter _router;
        private readonly EventSubscriber<TEvent> _subscriber;
        private readonly CancellationTokenSource _cts;
        private readonly Task _task;
        private int _disposed;

        public HandlerSubscription(
            EventChannelRouter router,
            EventSubscriber<TEvent> subscriber,
            CancellationTokenSource cts,
            Task task)
        {
            _router = router;
            _subscriber = subscriber;
            _cts = cts;
            _task = task;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _router.RemoveSubscriber(_subscriber.Id);
            _cts.Cancel();
            try
            {
                _task.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _cts.Dispose();
            }
        }
    }
}
