namespace HPD.Events.Struct;

/// <summary>Typed local lane for one struct event type.</summary>
internal interface IStructEventRoute : IDisposable
{
    StructEventRouteStats GetUntypedStats();
}

/// <summary>Typed local lane for one struct event type.</summary>
public sealed class StructEventRoute<TEvent> : IStructEventRoute
    where TEvent : struct, IStructEvent
{
    private readonly object _subscriberGate = new();
    private readonly StructEventRouteOptions _options;
    private StructEventSubscriber<TEvent>[] _subscribers = [];
    private long _sequenceCounter;
    private long _emitted;
    private long _accepted;
    private long _dropped;
    private long _filtered;
    private long _subscriberWrites;
    private long _subscriberDrops;
    private int _depth;
    private int _maxDepth;
    private int _disposed;

    /// <summary>Create a route with route-wide concurrency and statistics options.</summary>
    public StructEventRoute(StructEventRouteOptions? options = null)
    {
        _options = options ?? new StructEventRouteOptions();
    }

    internal StructEventRouteOptions Options => _options;

    /// <summary>Create an unsequenced emitter bound to this route.</summary>
    public StructEventEmitter<TEvent> CreateEmitter(
        StructEventEmitterOptions<TEvent>? options = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return new StructEventEmitter<TEvent>(this, options);
    }

    /// <summary>Create a caller-owned inbox.</summary>
    public StructEventInbox<TEvent> CreateInbox(StructEventInboxOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        options ??= new StructEventInboxOptions();

        var subscriber = new StructEventSubscriber<TEvent>(
            Guid.NewGuid(),
            options.Capacity,
            options.OverflowMode,
            isInbox: true,
            TracksStats ? OnRead : null);

        AddSubscriber(subscriber);
        return new StructEventInbox<TEvent>(this, subscriber);
    }

    /// <summary>Create a direct reader subscription.</summary>
    public StructEventSubscription<TEvent> Subscribe(StructEventSubscriptionOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        options ??= new StructEventSubscriptionOptions();

        var subscriber = new StructEventSubscriber<TEvent>(
            Guid.NewGuid(),
            options.Capacity,
            options.OverflowMode,
            isInbox: false,
            TracksStats ? OnRead : null);

        AddSubscriber(subscriber);
        return new StructEventSubscription<TEvent>(this, subscriber);
    }

    /// <summary>Get current route statistics.</summary>
    public StructEventRouteStats GetStats()
    {
        var subscribers = Volatile.Read(ref _subscribers);
        var inboxes = 0;
        for (var i = 0; i < subscribers.Length; i++)
        {
            if (subscribers[i].IsInbox)
                inboxes++;
        }

        return new StructEventRouteStats(
            typeof(TEvent),
            subscribers.Length,
            inboxes,
            Volatile.Read(ref _depth),
            Volatile.Read(ref _maxDepth),
            Volatile.Read(ref _emitted),
            Volatile.Read(ref _accepted),
            Volatile.Read(ref _dropped),
            Volatile.Read(ref _filtered),
            Volatile.Read(ref _subscriberWrites),
            Volatile.Read(ref _subscriberDrops));
    }

    StructEventRouteStats IStructEventRoute.GetUntypedStats() => GetStats();

    internal long NextSequence() => Interlocked.Increment(ref _sequenceCounter);

    internal StructEventEmitResult RecordFiltered()
    {
        if (TracksStats)
            Interlocked.Increment(ref _filtered);

        return new StructEventEmitResult(StructEventEmitStatus.Filtered, 0, 0, 0);
    }

    internal StructEventEmitResult EmitPrepared(in TEvent evt)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return new StructEventEmitResult(StructEventEmitStatus.Disposed, 0, 0, 0);

        if (TracksStats)
            Interlocked.Increment(ref _emitted);

        var subscribers = Volatile.Read(ref _subscribers);
        if (subscribers.Length == 0)
            return new StructEventEmitResult(StructEventEmitStatus.NoSubscribers, 0, 0, 0);

        var accepted = 0;
        var dropped = 0;
        var backpressured = 0;
        var rejected = 0;

        for (var i = 0; i < subscribers.Length; i++)
        {
            var write = subscribers[i].TryWrite(in evt);
            if (write.DroppedCount > 0)
            {
                dropped += write.DroppedCount;
                if (TracksStats)
                    Interlocked.Add(ref _subscriberDrops, write.DroppedCount);
            }

            switch (write.Status)
            {
                case StructEventWriteStatus.Accepted:
                    accepted++;
                    OnAcceptedWrite(write.DepthDelta);
                    break;
                case StructEventWriteStatus.Dropped:
                    break;
                case StructEventWriteStatus.Backpressured:
                    backpressured++;
                    break;
                case StructEventWriteStatus.Rejected:
                    rejected++;
                    break;
                case StructEventWriteStatus.Disposed:
                    dropped++;
                    break;
            }
        }

        if (accepted > 0)
        {
            if (TracksStats)
                Interlocked.Increment(ref _accepted);

            return new StructEventEmitResult(StructEventEmitStatus.Accepted, subscribers.Length, accepted, dropped);
        }

        if (backpressured > 0)
            return new StructEventEmitResult(StructEventEmitStatus.Backpressured, subscribers.Length, 0, dropped);

        if (rejected > 0)
            return new StructEventEmitResult(StructEventEmitStatus.Rejected, subscribers.Length, 0, dropped);

        if (TracksStats)
            Interlocked.Increment(ref _dropped);

        return new StructEventEmitResult(StructEventEmitStatus.Dropped, subscribers.Length, 0, dropped);
    }

    internal void RemoveSubscriber(Guid subscriberId)
    {
        StructEventSubscriber<TEvent>? removed = null;
        lock (_subscriberGate)
        {
            var current = _subscribers;
            var index = Array.FindIndex(current, subscriber => subscriber.Id == subscriberId);
            if (index < 0)
                return;

            removed = current[index];
            var next = new StructEventSubscriber<TEvent>[current.Length - 1];
            if (index > 0)
                Array.Copy(current, 0, next, 0, index);
            if (index < current.Length - 1)
                Array.Copy(current, index + 1, next, index, current.Length - index - 1);

            Volatile.Write(ref _subscribers, next);
        }

        if (TracksStats)
            OnRead(removed.Count);

        removed.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        StructEventSubscriber<TEvent>[] subscribers;
        lock (_subscriberGate)
        {
            subscribers = _subscribers;
            Volatile.Write(ref _subscribers, []);
        }

        for (var i = 0; i < subscribers.Length; i++)
        {
            if (TracksStats)
                OnRead(subscribers[i].Count);

            subscribers[i].Dispose();
        }
    }

    private void AddSubscriber(StructEventSubscriber<TEvent> subscriber)
    {
        lock (_subscriberGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            var current = _subscribers;
            var next = new StructEventSubscriber<TEvent>[current.Length + 1];
            Array.Copy(current, next, current.Length);
            next[^1] = subscriber;
            Volatile.Write(ref _subscribers, next);
        }
    }

    private void OnAcceptedWrite(int depthDelta)
    {
        if (!TracksStats)
            return;

        Interlocked.Increment(ref _subscriberWrites);
        if (depthDelta == 0)
            return;

        var current = Interlocked.Add(ref _depth, depthDelta);
        if (depthDelta > 0)
            UpdateMaxDepth(current);
    }

    private void OnRead(int count)
    {
        if (count <= 0)
            return;

        Interlocked.Add(ref _depth, -count);
    }

    private void UpdateMaxDepth(int current)
    {
        while (true)
        {
            var max = Volatile.Read(ref _maxDepth);
            if (current <= max)
                return;

            if (Interlocked.CompareExchange(ref _maxDepth, current, max) == max)
                return;
        }
    }

    private bool TracksStats => _options.StatsMode != StructEventStatsMode.None;
}
