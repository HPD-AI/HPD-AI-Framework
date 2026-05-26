using HPD.Events.Core;

namespace HPD.Events;

/// <summary>Typed local lane for one struct event type.</summary>
internal interface ILocalStructEventRoute : IDisposable
{
    LocalStructEventTypeStats GetUntypedStats();
}

/// <summary>Typed local lane for one struct event type.</summary>
public sealed class LocalStructEventRoute<TEvent> : ILocalStructEventRoute
    where TEvent : struct, IStructEvent
{
    private readonly object _subscriberGate = new();
    private LocalStructSubscriber<TEvent>[] _subscribers = [];
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

    /// <summary>Create an unsequenced emitter bound to this route.</summary>
    public LocalStructEmitter<TEvent> CreateEmitter(
        LocalStructEmitterOptions<TEvent>? options = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return new LocalStructEmitter<TEvent>(this, options);
    }

    /// <summary>Create a caller-owned inbox.</summary>
    public LocalStructInbox<TEvent> CreateInbox(LocalStructInboxOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        options ??= new LocalStructInboxOptions();

        var subscriber = new LocalStructSubscriber<TEvent>(
            Guid.NewGuid(),
            options.Capacity,
            options.FullMode,
            isInbox: true,
            isObserver: false,
            OnRead);

        AddSubscriber(subscriber);
        return new LocalStructInbox<TEvent>(this, subscriber);
    }

    /// <summary>Create a direct reader subscription.</summary>
    public LocalStructSubscription<TEvent> Subscribe(LocalStructSubscriptionOptions? options = null)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        options ??= new LocalStructSubscriptionOptions();

        var subscriber = new LocalStructSubscriber<TEvent>(
            Guid.NewGuid(),
            options.Capacity,
            options.FullMode,
            isInbox: false,
            isObserver: false,
            OnRead);

        AddSubscriber(subscriber);
        return new LocalStructSubscription<TEvent>(this, subscriber);
    }

    /// <summary>
    /// Register a synchronous local observer. The handler runs on the emitting thread.
    /// </summary>
    public IDisposable Observe(
        Func<TEvent, ValueTask> handler,
        LocalStructSubscriptionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        options ??= new LocalStructSubscriptionOptions();

        var observer = new LocalStructObserver<TEvent>(
            handler,
            options.Capacity,
            options.FullMode,
            OnRead);
        AddSubscriber(observer);
        return new LocalStructObserverHandle<TEvent>(this, observer.Id);
    }

    /// <summary>Get current route statistics.</summary>
    public LocalStructEventTypeStats GetStats()
    {
        var subscribers = Volatile.Read(ref _subscribers);
        var inboxes = 0;
        var observers = 0;
        for (var i = 0; i < subscribers.Length; i++)
        {
            if (subscribers[i].IsInbox)
                inboxes++;
            if (subscribers[i].IsObserver)
                observers++;
        }

        return new LocalStructEventTypeStats(
            typeof(TEvent),
            subscribers.Length,
            inboxes,
            observers,
            Volatile.Read(ref _depth),
            Volatile.Read(ref _maxDepth),
            Volatile.Read(ref _emitted),
            Volatile.Read(ref _accepted),
            Volatile.Read(ref _dropped),
            Volatile.Read(ref _filtered),
            Volatile.Read(ref _subscriberWrites),
            Volatile.Read(ref _subscriberDrops));
    }

    LocalStructEventTypeStats ILocalStructEventRoute.GetUntypedStats() => GetStats();

    internal long NextSequence() => Interlocked.Increment(ref _sequenceCounter);

    internal LocalStructEmitResult RecordFiltered()
    {
        Interlocked.Increment(ref _filtered);
        return new LocalStructEmitResult(LocalStructEmitStatus.Filtered, 0, 0, 0);
    }

    internal LocalStructEmitResult EmitPrepared(in TEvent evt)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return new LocalStructEmitResult(LocalStructEmitStatus.Disposed, 0, 0, 0);

        Interlocked.Increment(ref _emitted);

        var subscribers = Volatile.Read(ref _subscribers);
        if (subscribers.Length == 0)
            return new LocalStructEmitResult(LocalStructEmitStatus.NoSubscribers, 0, 0, 0);

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
                Interlocked.Add(ref _subscriberDrops, write.DroppedCount);
            }

            switch (write.Status)
            {
                case LocalStructWriteStatus.Accepted:
                    accepted++;
                    OnAcceptedWrite(write.DepthDelta);
                    if (subscribers[i] is LocalStructObserver<TEvent> observer)
                    {
                        try
                        {
                            observer.Drain();
                        }
                        catch
                        {
                            RemoveSubscriber(observer.Id);
                        }
                    }
                    break;
                case LocalStructWriteStatus.Dropped:
                    dropped++;
                    break;
                case LocalStructWriteStatus.Backpressured:
                    backpressured++;
                    break;
                case LocalStructWriteStatus.Rejected:
                    rejected++;
                    break;
                case LocalStructWriteStatus.Disposed:
                    dropped++;
                    break;
            }
        }

        if (accepted > 0)
        {
            Interlocked.Increment(ref _accepted);
            return new LocalStructEmitResult(LocalStructEmitStatus.Accepted, subscribers.Length, accepted, dropped);
        }

        if (backpressured > 0)
            return new LocalStructEmitResult(LocalStructEmitStatus.Backpressured, subscribers.Length, 0, dropped);

        if (rejected > 0)
            return new LocalStructEmitResult(LocalStructEmitStatus.Rejected, subscribers.Length, 0, dropped);

        Interlocked.Increment(ref _dropped);
        return new LocalStructEmitResult(LocalStructEmitStatus.Dropped, subscribers.Length, 0, dropped);
    }

    internal void RemoveSubscriber(Guid subscriberId)
    {
        LocalStructSubscriber<TEvent>? removed = null;
        lock (_subscriberGate)
        {
            var current = _subscribers;
            var index = Array.FindIndex(current, subscriber => subscriber.Id == subscriberId);
            if (index < 0)
                return;

            removed = current[index];
            var next = new LocalStructSubscriber<TEvent>[current.Length - 1];
            if (index > 0)
                Array.Copy(current, 0, next, 0, index);
            if (index < current.Length - 1)
                Array.Copy(current, index + 1, next, index, current.Length - index - 1);

            Volatile.Write(ref _subscribers, next);
        }

        OnRead(removed.Count);
        removed.Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        LocalStructSubscriber<TEvent>[] subscribers;
        lock (_subscriberGate)
        {
            subscribers = _subscribers;
            Volatile.Write(ref _subscribers, []);
        }

        for (var i = 0; i < subscribers.Length; i++)
        {
            OnRead(subscribers[i].Count);
            subscribers[i].Dispose();
        }
    }

    private void AddSubscriber(LocalStructSubscriber<TEvent> subscriber)
    {
        lock (_subscriberGate)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            var current = _subscribers;
            var next = new LocalStructSubscriber<TEvent>[current.Length + 1];
            Array.Copy(current, next, current.Length);
            next[^1] = subscriber;
            Volatile.Write(ref _subscribers, next);
        }
    }

    private void OnAcceptedWrite(int depthDelta)
    {
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
}

internal sealed class LocalStructObserver<TEvent> : LocalStructSubscriber<TEvent>
    where TEvent : struct, IStructEvent
{
    private readonly Func<TEvent, ValueTask> _handler;
    private int _draining;

    public LocalStructObserver(
        Func<TEvent, ValueTask> handler,
        int capacity,
        LocalStructFullMode fullMode,
        Action<int> onRead)
        : base(
            Guid.NewGuid(),
            capacity,
            fullMode,
            isInbox: false,
            isObserver: true,
            onRead)
    {
        _handler = handler;
    }

    public void Drain()
    {
        if (Interlocked.Exchange(ref _draining, 1) != 0)
            return;

        try
        {
            while (TryRead(out var evt))
            {
                var task = _handler(evt);
                if (!task.IsCompletedSuccessfully)
                    task.AsTask().GetAwaiter().GetResult();
            }
        }
        finally
        {
            Volatile.Write(ref _draining, 0);
        }
    }

}

internal sealed class LocalStructObserverHandle<TEvent> : IDisposable
    where TEvent : struct, IStructEvent
{
    private readonly LocalStructEventRoute<TEvent> _route;
    private readonly Guid _subscriberId;
    private int _disposed;

    public LocalStructObserverHandle(LocalStructEventRoute<TEvent> route, Guid subscriberId)
    {
        _route = route;
        _subscriberId = subscriberId;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _route.RemoveSubscriber(_subscriberId);
    }
}
