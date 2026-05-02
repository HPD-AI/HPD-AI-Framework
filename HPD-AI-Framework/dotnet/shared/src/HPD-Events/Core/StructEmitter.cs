using HPD.Events.Core;

namespace HPD.Events;

/// <summary>
/// Pre-bound hot-path emitter for one local struct-event type.
/// </summary>
public readonly struct StructEmitter<TEvent>
    where TEvent : struct, IStructEvent
{
    private readonly StructEventRouter? _router;
    private readonly StructEmitterOptions<TEvent>? _options;

    internal StructEmitter(StructEventRouter router, StructEmitterOptions<TEvent>? options)
    {
        _router = router;
        _options = options;
    }

    /// <summary>Try to emit without waiting for subscriber capacity.</summary>
    public bool TryEmit(in TEvent evt)
    {
        if (_router is null)
            return false;

        if (!TryPrepare(evt, out var outgoing))
            return false;

        return _router.TryEmitStruct(in outgoing);
    }

    /// <summary>Emit asynchronously, waiting when a subscriber requests backpressure.</summary>
    public ValueTask EmitAsync(TEvent evt, CancellationToken ct = default)
    {
        if (_router is null)
            return ValueTask.CompletedTask;

        if (!TryPrepare(evt, out var outgoing))
            return ValueTask.CompletedTask;

        return _router.EmitStructAsync(outgoing, ct);
    }

    private bool TryPrepare(TEvent evt, out TEvent outgoing)
    {
        outgoing = evt;

        if (_options?.Filter is { } filter && !filter(evt))
            return false;

        if (_options?.AssignSequenceNumbers == true && outgoing is ISequencedStructEvent<TEvent> sequenced)
            outgoing = sequenced.WithSequenceNumber(_router?.NextSequence() ?? 0);

        return true;
    }
}
