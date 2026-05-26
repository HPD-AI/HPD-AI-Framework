namespace HPD.Events;

/// <summary>Route-bound emitter for one local struct event type.</summary>
public readonly struct LocalStructEmitter<TEvent>
    where TEvent : struct, IStructEvent
{
    private readonly LocalStructEventRoute<TEvent>? _route;
    private readonly LocalStructEmitterOptions<TEvent>? _options;

    internal LocalStructEmitter(
        LocalStructEventRoute<TEvent> route,
        LocalStructEmitterOptions<TEvent>? options)
    {
        _route = route;
        _options = options;
    }

    /// <summary>Emit one event synchronously.</summary>
    public LocalStructEmitResult Emit(in TEvent evt)
    {
        if (_route is null)
            return new LocalStructEmitResult(LocalStructEmitStatus.Disposed, 0, 0, 0);

        if (_options?.Filter is { } filter && !filter(evt))
            return _route.RecordFiltered();

        return _route.EmitPrepared(in evt);
    }

    /// <summary>Emit a batch of events synchronously.</summary>
    public LocalStructEmitBatchResult EmitBatch(ReadOnlySpan<TEvent> events)
    {
        var result = new LocalStructBatchAccumulator(events.Length);
        for (var i = 0; i < events.Length; i++)
            result.Add(Emit(in events[i]));

        return result.ToResult();
    }
}

/// <summary>Route-bound sequenced emitter for one local struct event type.</summary>
public readonly struct LocalSequencedStructEmitter<TEvent>
    where TEvent : struct, IStructEvent, ISequencedStructEvent<TEvent>
{
    private readonly LocalStructEventRoute<TEvent>? _route;
    private readonly LocalStructEmitterOptions<TEvent>? _options;

    internal LocalSequencedStructEmitter(
        LocalStructEventRoute<TEvent> route,
        LocalStructEmitterOptions<TEvent>? options)
    {
        _route = route;
        _options = options;
    }

    /// <summary>Assign a route sequence number and emit one event synchronously.</summary>
    public LocalStructEmitResult Emit(in TEvent evt)
    {
        if (_route is null)
            return new LocalStructEmitResult(LocalStructEmitStatus.Disposed, 0, 0, 0);

        if (_options?.Filter is { } filter && !filter(evt))
            return _route.RecordFiltered();

        var outgoing = evt.WithSequenceNumber(_route.NextSequence());
        return _route.EmitPrepared(in outgoing);
    }

    /// <summary>Assign route sequence numbers and emit a batch synchronously.</summary>
    public LocalStructEmitBatchResult EmitBatch(ReadOnlySpan<TEvent> events)
    {
        var result = new LocalStructBatchAccumulator(events.Length);
        for (var i = 0; i < events.Length; i++)
            result.Add(Emit(in events[i]));

        return result.ToResult();
    }
}

internal struct LocalStructBatchAccumulator
{
    private readonly int _eventCount;
    private int _acceptedEvents;
    private int _droppedEvents;
    private int _backpressuredEvents;
    private int _rejectedEvents;
    private int _filteredEvents;
    private int _totalSubscriberWrites;
    private int _totalSubscriberDrops;

    public LocalStructBatchAccumulator(int eventCount) => _eventCount = eventCount;

    public void Add(LocalStructEmitResult result)
    {
        switch (result.Status)
        {
            case LocalStructEmitStatus.Accepted:
                _acceptedEvents++;
                break;
            case LocalStructEmitStatus.Filtered:
                _filteredEvents++;
                break;
            case LocalStructEmitStatus.Backpressured:
                _backpressuredEvents++;
                break;
            case LocalStructEmitStatus.Rejected:
                _rejectedEvents++;
                break;
            case LocalStructEmitStatus.Dropped:
                _droppedEvents++;
                break;
        }

        _totalSubscriberWrites += result.AcceptedCount;
        _totalSubscriberDrops += result.DroppedCount;
    }

    public LocalStructEmitBatchResult ToResult() =>
        new(
            _eventCount,
            _acceptedEvents,
            _droppedEvents,
            _backpressuredEvents,
            _rejectedEvents,
            _filteredEvents,
            _totalSubscriberWrites,
            _totalSubscriberDrops);
}
