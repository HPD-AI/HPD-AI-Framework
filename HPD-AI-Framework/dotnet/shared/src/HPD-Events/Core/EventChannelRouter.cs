using System.Collections.Concurrent;
using System.Threading.Channels;

namespace HPD.Events.Core;

/// <summary>
/// Routes semantic HPD events through per-subscriber fan-out mailboxes.
/// </summary>
internal sealed class EventChannelRouter : IDisposable
{
    private readonly ConcurrentDictionary<string, (TaskCompletionSource<Event>, CancellationTokenSource)>
        _responseWaiters = new();
    private readonly ConcurrentDictionary<EventChannelRouter, byte> _children = new();
    private readonly List<IClassEventSubscriber> _subscribers = new();
    private readonly StreamRegistry _streamRegistry = new();
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

    public IStreamRegistry Streams => _streamRegistry;

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

        var subscriber = CreateSubscriber<TEvent>(isStream: false, options);
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

    public EventStreamSubscription<TEvent> SubscribeStream<TEvent>(
        EventSubscriptionOptions? options = null)
        where TEvent : Event
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var subscriber = CreateSubscriber<TEvent>(isStream: true, options);
        return new EventStreamSubscription<TEvent>(
            subscriber.Reader,
            subscriber.Writer,
            writer => RemoveSubscriberByWriter(writer));
    }

    public EventStreamSubscription<Event> SubscribeChannel(
        EventChannel channel,
        EventSubscriptionOptions? options = null)
    {
        options = (options ?? new EventSubscriptionOptions()) with { Channel = channel };
        return SubscribeStream<Event>(options);
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

    internal async Task<TResponse> WaitForResponseAsync<TResponse>(
        string requestId,
        TimeSpan timeout,
        CancellationToken ct = default) where TResponse : Event
    {
        if (string.IsNullOrWhiteSpace(requestId))
            throw new ArgumentException("Request ID cannot be null or whitespace", nameof(requestId));

        ObjectDisposedException.ThrowIf(_disposed, this);

        var tcs = new TaskCompletionSource<Event>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        if (!_responseWaiters.TryAdd(requestId, (tcs, cts)))
            throw new InvalidOperationException($"Duplicate request ID: {requestId}");

        try
        {
            cts.CancelAfter(timeout);
            using var registration = cts.Token.Register(() =>
            {
                _responseWaiters.TryRemove(requestId, out _);
                tcs.TrySetCanceled(cts.Token);
            });

            var response = await tcs.Task.ConfigureAwait(false);

            if (response is not TResponse typedResponse)
            {
                throw new InvalidOperationException(
                    $"Response type mismatch. Expected {typeof(TResponse).Name}, got {response.GetType().Name}");
            }

            return typedResponse;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"No response received for request ID '{requestId}' within {timeout.TotalSeconds:F1}s");
        }
        finally
        {
            _responseWaiters.TryRemove(requestId, out _);
            cts.Dispose();
        }
    }

    public bool TryRespond(string requestId, Event response)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            throw new ArgumentException("Request ID cannot be null or whitespace", nameof(requestId));

        ArgumentNullException.ThrowIfNull(response);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var matches = new List<EventChannelRouter>();
        FindResponseWaiters(requestId, matches);

        if (matches.Count == 0)
            return false;

        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                $"Multiple pending response waiters found for request ID '{requestId}' in the coordinator hierarchy.");
        }

        matches[0].CompleteLocalResponse(requestId, response);
        return true;
    }

    public EventCoordinatorStats GetStats()
    {
        IClassEventSubscriber[] subscribers;
        lock (_subscribers)
        {
            subscribers = _subscribers.ToArray();
        }

        var totalQueued = 0;
        var maxDepth = 0;
        var streamSubscribers = 0;
        foreach (var subscriber in subscribers)
        {
            var depth = subscriber.Depth;
            totalQueued += depth;
            maxDepth = Math.Max(maxDepth, depth);
            if (subscriber.IsStream)
                streamSubscribers++;
        }

        return new EventCoordinatorStats(
            subscribers.Length,
            streamSubscribers,
            totalQueued,
            (int)Math.Min(int.MaxValue, Volatile.Read(ref _totalDropped)),
            maxDepth);
    }

    private EventSubscriber<TEvent> CreateSubscriber<TEvent>(
        bool isStream,
        EventSubscriptionOptions? options)
        where TEvent : Event
    {
        options ??= new EventSubscriptionOptions();
        if (options.Capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Event subscription capacity must be greater than zero.");

        var subscriber = new EventSubscriber<TEvent>(options, isStream, OnSubscriberDropped);
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
        if (enriched.SequenceNumber == 0)
            enriched.SequenceNumber = Interlocked.Increment(ref _sequenceCounter);

        if (enriched.StreamId is not null && enriched.CanInterrupt && enriched is not EventDroppedEvent)
        {
            var streamHandle = _streamRegistry.Get(enriched.StreamId);
            if (streamHandle is StreamHandle { IsInterrupted: true } handle)
            {
                handle.IncrementDroppedCount();
                PublishDiagnostic(new EventDroppedEvent(
                    enriched.StreamId,
                    enriched.GetType().Name,
                    enriched.SequenceNumber),
                    skipSubscriberId: null);
                return null;
            }
        }

        if (enriched.StreamId is not null && enriched.CanInterrupt && enriched is not EventDroppedEvent)
        {
            if (_streamRegistry.Get(enriched.StreamId) is StreamHandle handle)
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
        if (diagnostic.SequenceNumber == 0)
            diagnostic.SequenceNumber = Interlocked.Increment(ref _sequenceCounter);

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

    private void FindResponseWaiters(string requestId, List<EventChannelRouter> matches)
    {
        if (_disposed)
            return;

        if (_responseWaiters.ContainsKey(requestId))
            matches.Add(this);

        foreach (var child in _children.Keys)
            child.FindResponseWaiters(requestId, matches);
    }

    private void CompleteLocalResponse(string requestId, Event response)
    {
        if (_responseWaiters.TryRemove(requestId, out var waiter))
        {
            waiter.Item1.TrySetResult(response);
            waiter.Item2.Dispose();
        }
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

        foreach (var (_, (tcs, cts)) in _responseWaiters)
        {
            tcs.TrySetCanceled();
            cts.Dispose();
        }

        _responseWaiters.Clear();
        _children.Clear();
    }

    private interface IClassEventSubscriber
    {
        Guid Id { get; }
        EventSubscriptionOptions Options { get; }
        bool IsStream { get; }
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
            bool isStream,
            Action onDropped)
        {
            Options = options;
            IsStream = isStream;
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
        public bool IsStream { get; }
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
