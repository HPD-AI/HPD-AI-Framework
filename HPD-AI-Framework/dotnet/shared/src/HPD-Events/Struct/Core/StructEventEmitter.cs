namespace HPD.Events.Struct;

/// <summary>Route-bound emitter for one struct event event type.</summary>
public readonly struct StructEventEmitter<TEvent>
    where TEvent : struct, IStructEvent
{
    private readonly StructEventRoute<TEvent>? _route;
    private readonly StructEventEmitterOptions<TEvent>? _options;

    internal StructEventEmitter(
        StructEventRoute<TEvent> route,
        StructEventEmitterOptions<TEvent>? options)
    {
        _route = route;
        _options = options;
    }

    /// <summary>Emit one event synchronously.</summary>
    public StructEventEmitResult Emit(in TEvent evt)
    {
        if (_route is null)
            return new StructEventEmitResult(StructEventEmitStatus.Disposed, 0, 0, 0);

        if (_options?.Filter is { } filter && !filter(evt))
            return _route.RecordFiltered();

        return _route.EmitPrepared(in evt);
    }

    /// <summary>Emit a batch of events synchronously.</summary>
    public StructEventEmitBatchResult EmitBatch(ReadOnlySpan<TEvent> events)
    {
        var result = new StructEventBatchAccumulator(events.Length);
        for (var i = 0; i < events.Length; i++)
            result.Add(Emit(in events[i]));

        return result.ToResult();
    }
}

/// <summary>Route-bound sequenced emitter for one struct event event type.</summary>
public readonly struct SequencedStructEventEmitter<TEvent>
    where TEvent : struct, IStructEvent, ISequencedStructEvent<TEvent>
{
    private readonly StructEventRoute<TEvent>? _route;
    private readonly StructEventEmitterOptions<TEvent>? _options;

    internal SequencedStructEventEmitter(
        StructEventRoute<TEvent> route,
        StructEventEmitterOptions<TEvent>? options)
    {
        _route = route;
        _options = options;
    }

    /// <summary>Assign a route sequence number and emit one event synchronously.</summary>
    public StructEventEmitResult Emit(in TEvent evt)
    {
        if (_route is null)
            return new StructEventEmitResult(StructEventEmitStatus.Disposed, 0, 0, 0);

        if (_options?.Filter is { } filter && !filter(evt))
            return _route.RecordFiltered();

        var outgoing = evt.WithSequenceNumber(_route.NextSequence());
        return _route.EmitPrepared(in outgoing);
    }

    /// <summary>Assign route sequence numbers and emit a batch synchronously.</summary>
    public StructEventEmitBatchResult EmitBatch(ReadOnlySpan<TEvent> events)
    {
        var result = new StructEventBatchAccumulator(events.Length);
        for (var i = 0; i < events.Length; i++)
            result.Add(Emit(in events[i]));

        return result.ToResult();
    }
}

internal struct StructEventBatchAccumulator
{
    private readonly int _eventCount;
    private int _acceptedEvents;
    private int _droppedEvents;
    private int _backpressuredEvents;
    private int _rejectedEvents;
    private int _filteredEvents;
    private int _totalSubscriberWrites;
    private int _totalSubscriberDrops;

    public StructEventBatchAccumulator(int eventCount) => _eventCount = eventCount;

    public void Add(StructEventEmitResult result)
    {
        switch (result.Status)
        {
            case StructEventEmitStatus.Accepted:
                _acceptedEvents++;
                break;
            case StructEventEmitStatus.Filtered:
                _filteredEvents++;
                break;
            case StructEventEmitStatus.Backpressured:
                _backpressuredEvents++;
                break;
            case StructEventEmitStatus.Rejected:
                _rejectedEvents++;
                break;
            case StructEventEmitStatus.Dropped:
                _droppedEvents++;
                break;
        }

        _totalSubscriberWrites += result.AcceptedCount;
        _totalSubscriberDrops += result.DroppedCount;
    }

    public StructEventEmitBatchResult ToResult() =>
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
