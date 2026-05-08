using System.Collections.Concurrent;
using System.Threading.Channels;

namespace HPD.Events.Core;

/// <summary>
/// Routes local hot-path struct events through exact-type subscriber lists.
/// </summary>
internal sealed class StructEventRouter : IDisposable
{
    private readonly ConcurrentDictionary<Type, object> _subscribersByType = new();
    private readonly List<RegisteredStructHandler> _handlerPumps = new();
    private long _sequenceCounter;
    private bool _disposed;

    public bool TryEmitStruct<TEvent>(in TEvent evt)
        where TEvent : struct, IStructEvent
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_subscribersByType.TryGetValue(typeof(TEvent), out var boxed))
            return false;

        var subscribers = (List<StructSubscriber<TEvent>>)boxed;
        StructSubscriber<TEvent>[] snapshot;
        lock (subscribers)
        {
            snapshot = subscribers.ToArray();
        }

        var accepted = false;
        foreach (var subscriber in snapshot)
            accepted |= subscriber.Writer.TryWrite(evt);

        return accepted;
    }

    public async ValueTask EmitStructAsync<TEvent>(TEvent evt, CancellationToken ct = default)
        where TEvent : struct, IStructEvent
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_subscribersByType.TryGetValue(typeof(TEvent), out var boxed))
            return;

        var subscribers = (List<StructSubscriber<TEvent>>)boxed;
        StructSubscriber<TEvent>[] snapshot;
        lock (subscribers)
        {
            snapshot = subscribers.ToArray();
        }

        foreach (var subscriber in snapshot)
        {
            if (subscriber.Writer.TryWrite(evt))
                continue;

            if (subscriber.Options.FullMode == BoundedChannelFullMode.Wait)
                await subscriber.Writer.WriteAsync(evt, ct).ConfigureAwait(false);
        }
    }

    public IDisposable SubscribeStruct<TEvent>(Func<TEvent, ValueTask> handler)
        where TEvent : struct, IStructEvent
    {
        ArgumentNullException.ThrowIfNull(handler);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var subscription = SubscribeStruct<TEvent>();
        var registration = new RegisteredStructHandler(ct => RunHandlerPumpAsync(subscription, handler, ct));
        lock (_handlerPumps)
        {
            _handlerPumps.Add(registration);
        }

        return new StructHandlerSubscription(this, registration.Id, subscription.DisposeAsync);
    }

    public StructSubscription<TEvent> SubscribeStruct<TEvent>(StructSubscriptionOptions? options = null)
        where TEvent : struct, IStructEvent
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        options ??= new StructSubscriptionOptions();
        if (options.Capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Struct subscription capacity must be greater than zero.");

        var channel = Channel.CreateBounded<TEvent>(new BoundedChannelOptions(options.Capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = options.FullMode,
            AllowSynchronousContinuations = false
        });

        var subscriber = new StructSubscriber<TEvent>(channel.Writer, options);
        var subscribers = GetSubscribers<TEvent>();
        lock (subscribers)
        {
            subscribers.Add(subscriber);
        }

        return new StructSubscription<TEvent>(
            channel.Reader,
            channel.Writer,
            writer => RemoveSubscriber<TEvent>(writer));
    }

    public StructEmitter<TEvent> CreateStructEmitter<TEvent>(StructEmitterOptions<TEvent>? options = null)
        where TEvent : struct, IStructEvent =>
        new(this, options);

    public async Task RunAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Func<CancellationToken, Task>[] pumps;
        lock (_handlerPumps)
        {
            pumps = _handlerPumps.Select(handler => handler.Pump).ToArray();
        }

        if (pumps.Length == 0)
            return;

        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var tasks = pumps.Select(pump => pump(runCts.Token)).ToList();

        while (tasks.Count > 0)
        {
            var completed = await Task.WhenAny(tasks).ConfigureAwait(false);
            if (completed.IsFaulted)
            {
                await runCts.CancelAsync().ConfigureAwait(false);
                await completed.ConfigureAwait(false);
            }

            tasks.Remove(completed);
        }
    }

    public long NextSequence() => Interlocked.Increment(ref _sequenceCounter);

    private void RemoveHandler(Guid handlerId)
    {
        lock (_handlerPumps)
        {
            _handlerPumps.RemoveAll(handler => handler.Id == handlerId);
        }
    }

    private static async Task RunHandlerPumpAsync<TEvent>(
        StructSubscription<TEvent> subscription,
        Func<TEvent, ValueTask> handler,
        CancellationToken ct)
        where TEvent : struct, IStructEvent
    {
        try
        {
            await foreach (var evt in subscription.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                await handler(evt).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cooperative shutdown.
        }
        finally
        {
            await subscription.DisposeAsync().ConfigureAwait(false);
        }
    }

    private List<StructSubscriber<TEvent>> GetSubscribers<TEvent>()
        where TEvent : struct, IStructEvent =>
        (List<StructSubscriber<TEvent>>)_subscribersByType.GetOrAdd(
            typeof(TEvent),
            _ => new List<StructSubscriber<TEvent>>());

    private void RemoveSubscriber<TEvent>(ChannelWriter<TEvent> writer)
        where TEvent : struct, IStructEvent
    {
        if (_subscribersByType.TryGetValue(typeof(TEvent), out var boxed))
        {
            var subscribers = (List<StructSubscriber<TEvent>>)boxed;
            lock (subscribers)
            {
                subscribers.RemoveAll(subscriber => ReferenceEquals(subscriber.Writer, writer));
            }
        }

        writer.TryComplete();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        foreach (var boxed in _subscribersByType.Values)
            CompleteSubscriberList(boxed);

        _subscribersByType.Clear();
    }

    private static void CompleteSubscriberList(object boxed)
    {
        if (boxed is IEnumerable<IStructSubscriber> genericSubscribers)
        {
            foreach (var subscriber in genericSubscribers)
                subscriber.Complete();
        }
    }

    private interface IStructSubscriber
    {
        void Complete();
    }

    private sealed class RegisteredStructHandler(Func<CancellationToken, Task> pump)
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Func<CancellationToken, Task> Pump { get; } = pump;
    }

    private sealed class StructHandlerSubscription(
        StructEventRouter router,
        Guid handlerId,
        Func<ValueTask> disposeSubscription) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            router.RemoveHandler(handlerId);
            disposeSubscription().AsTask().GetAwaiter().GetResult();
        }
    }

    private sealed record StructSubscriber<TEvent>(
        ChannelWriter<TEvent> Writer,
        StructSubscriptionOptions Options) : IStructSubscriber
        where TEvent : struct, IStructEvent
    {
        public void Complete() => Writer.TryComplete();
    }
}
