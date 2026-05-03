using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace HPD.Events.Core;

/// <summary>
/// Routes semantic HPD events through the four EventChannel queues.
/// </summary>
internal sealed class EventChannelRouter : IDisposable
{
    private readonly Channel<Event> _streamingChannel;
    private readonly Channel<Event> _synchronousChannel;
    private readonly Channel<Event> _interactiveChannel;
    private readonly Channel<Event> _controlChannel;

    private readonly ConcurrentDictionary<string, (TaskCompletionSource<Event>, CancellationTokenSource)>
        _responseWaiters = new();
    private readonly ConcurrentDictionary<EventChannelRouter, byte> _children = new();

    private readonly Dictionary<Type, List<Func<Event, ValueTask>>> _classHandlers = new();
    private readonly List<Func<Event, ValueTask>> _anyHandlers = new();

    private readonly StreamRegistry _streamRegistry = new();
    private readonly Func<Event, Event>? _eventEnricher;
    private readonly Func<Event, bool>? _eventFilter;

    private long _sequenceCounter;
    private int _streamingDepth;
    private int _synchronousDepth;
    private int _interactiveDepth;
    private int _controlDepth;
    private IEventCoordinator? _parentCoordinator;
    private bool _disposed;

    public EventChannelRouter(
        Func<Event, Event>? eventEnricher = null,
        Func<Event, bool>? eventFilter = null)
    {
        _eventEnricher = eventEnricher;
        _eventFilter = eventFilter;

        _controlChannel = Channel.CreateBounded<Event>(new BoundedChannelOptions(64)
        {
            SingleWriter = false,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });

        _streamingChannel = Channel.CreateBounded<Event>(
            new BoundedChannelOptions(256)
            {
                SingleWriter = false,
                SingleReader = true,
                FullMode = BoundedChannelFullMode.DropOldest,
                AllowSynchronousContinuations = false
            },
            itemDropped: evt =>
            {
                Interlocked.Decrement(ref _streamingDepth);
                if (_controlChannel.Writer.TryWrite(new EventDroppedEvent(
                    evt.StreamId ?? string.Empty,
                    evt.GetType().Name,
                    evt.SequenceNumber)))
                {
                    Interlocked.Increment(ref _controlDepth);
                }
            });

        _synchronousChannel = Channel.CreateUnbounded<Event>(new UnboundedChannelOptions
        {
            SingleWriter = false,
            SingleReader = true,
            AllowSynchronousContinuations = false
        });

        _interactiveChannel = Channel.CreateBounded<Event>(new BoundedChannelOptions(64)
        {
            SingleWriter = false,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
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

        if (!TryEmitToChannel(enriched))
        {
            throw new InvalidOperationException(
                $"Event channel '{enriched.Channel}' is full. Use EmitAsync() to wait for capacity.");
        }

        _parentCoordinator?.Emit(enriched);
    }

    public async ValueTask EmitAsync(Event evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var enriched = PrepareForEmission(evt);
        if (enriched is null)
            return;

        await WriteToChannelAsync(enriched, ct).ConfigureAwait(false);

        if (_parentCoordinator is not null)
            await _parentCoordinator.EmitAsync(enriched, ct).ConfigureAwait(false);
    }

    public void On<TEvent>(Func<TEvent, ValueTask> handler) where TEvent : Event
    {
        ArgumentNullException.ThrowIfNull(handler);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var type = typeof(TEvent);
        lock (_classHandlers)
        {
            if (!_classHandlers.TryGetValue(type, out var list))
            {
                list = new List<Func<Event, ValueTask>>();
                _classHandlers[type] = list;
            }

            list.Add(evt => handler((TEvent)evt));
        }
    }

    public void OnAny(Func<Event, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_anyHandlers)
        {
            _anyHandlers.Add(handler);
        }
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var tasks = new List<Task>
        {
            RunChannelAsync(_streamingChannel.Reader, () => Interlocked.Decrement(ref _streamingDepth), runCts.Token),
            RunChannelAsync(_synchronousChannel.Reader, () => Interlocked.Decrement(ref _synchronousDepth), runCts.Token),
            RunChannelAsync(_interactiveChannel.Reader, () => Interlocked.Decrement(ref _interactiveDepth), runCts.Token),
            RunChannelAsync(_controlChannel.Reader, () => Interlocked.Decrement(ref _controlDepth), runCts.Token)
        };

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

    public async Task<TResponse> WaitForResponseAsync<TResponse>(
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

    public bool SendResponse(string requestId, Event response)
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

    public EventCoordinatorStats GetStats() =>
        new(
            Volatile.Read(ref _streamingDepth),
            Volatile.Read(ref _synchronousDepth),
            Volatile.Read(ref _interactiveDepth),
            Volatile.Read(ref _controlDepth));

    public IAsyncEnumerable<Event> ReadStreamingAsync(CancellationToken ct = default) =>
        ReadChannelAsync(_streamingChannel.Reader, () => Interlocked.Decrement(ref _streamingDepth), ct);

    public IAsyncEnumerable<Event> ReadSynchronousAsync(CancellationToken ct = default) =>
        ReadChannelAsync(_synchronousChannel.Reader, () => Interlocked.Decrement(ref _synchronousDepth), ct);

    public IAsyncEnumerable<Event> ReadInteractiveAsync(CancellationToken ct = default) =>
        ReadChannelAsync(_interactiveChannel.Reader, () => Interlocked.Decrement(ref _interactiveDepth), ct);

    public IAsyncEnumerable<Event> ReadControlAsync(CancellationToken ct = default) =>
        ReadChannelAsync(_controlChannel.Reader, () => Interlocked.Decrement(ref _controlDepth), ct);

    private Event? PrepareForEmission(Event evt)
    {
        if (_eventFilter != null && !_eventFilter(evt))
            return null;

        var enriched = _eventEnricher?.Invoke(evt) ?? evt;
        enriched.SequenceNumber = Interlocked.Increment(ref _sequenceCounter);

        if (enriched.StreamId is not null && enriched.CanInterrupt && enriched is not EventDroppedEvent)
        {
            var streamHandle = _streamRegistry.Get(enriched.StreamId);
            if (streamHandle is StreamHandle { IsInterrupted: true } handle)
            {
                handle.IncrementDroppedCount();
                TryEmitToChannel(new EventDroppedEvent(
                    enriched.StreamId,
                    enriched.GetType().Name,
                    enriched.SequenceNumber));
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

    private bool TryEmitToChannel(Event evt)
    {
        var written = GetChannel(evt.Channel).Writer.TryWrite(evt);
        if (written)
            IncrementDepth(evt.Channel);

        return written;
    }

    private async ValueTask WriteToChannelAsync(Event evt, CancellationToken ct)
    {
        await GetChannel(evt.Channel).Writer.WriteAsync(evt, ct).ConfigureAwait(false);
        IncrementDepth(evt.Channel);
    }

    private void IncrementDepth(EventChannel channel)
    {
        switch (channel)
        {
            case EventChannel.Streaming:
                Interlocked.Increment(ref _streamingDepth);
                break;
            case EventChannel.Synchronous:
                Interlocked.Increment(ref _synchronousDepth);
                break;
            case EventChannel.Interactive:
                Interlocked.Increment(ref _interactiveDepth);
                break;
            case EventChannel.Control:
                Interlocked.Increment(ref _controlDepth);
                break;
        }
    }

    private Channel<Event> GetChannel(EventChannel channel) => channel switch
    {
        EventChannel.Streaming => _streamingChannel,
        EventChannel.Synchronous => _synchronousChannel,
        EventChannel.Interactive => _interactiveChannel,
        EventChannel.Control => _controlChannel,
        _ => _synchronousChannel
    };

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

    private async Task RunChannelAsync(ChannelReader<Event> reader, Action decrementDepth, CancellationToken ct)
    {
        try
        {
            await foreach (var evt in reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                decrementDepth();

                List<Func<Event, ValueTask>>? exactHandlers = null;
                lock (_classHandlers)
                {
                    if (_classHandlers.TryGetValue(evt.GetType(), out var handlers))
                        exactHandlers = handlers.ToList();
                }

                if (exactHandlers is not null)
                {
                    foreach (var handler in exactHandlers)
                        await handler(evt).ConfigureAwait(false);
                }

                List<Func<Event, ValueTask>> anyHandlers;
                lock (_anyHandlers)
                {
                    anyHandlers = _anyHandlers.ToList();
                }

                foreach (var handler in anyHandlers)
                    await handler(evt).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cooperative shutdown.
        }
    }

    private static async IAsyncEnumerable<Event> ReadChannelAsync(
        ChannelReader<Event> reader,
        Action decrementDepth,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var evt in reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            decrementDepth();
            yield return evt;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_parentCoordinator is EventCoordinator parent)
            parent.UnregisterChildRouter(this);

        _streamingChannel.Writer.TryComplete();
        _synchronousChannel.Writer.TryComplete();
        _interactiveChannel.Writer.TryComplete();
        _controlChannel.Writer.TryComplete();

        foreach (var (_, (tcs, cts)) in _responseWaiters)
        {
            tcs.TrySetCanceled();
            cts.Dispose();
        }

        _responseWaiters.Clear();
        _children.Clear();
    }
}
