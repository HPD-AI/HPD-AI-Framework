namespace HPD.Events;

/// <summary>
/// Process-local struct event surface for hot-path events.
/// </summary>
public interface IStructEventBus
{
    /// <summary>
    /// Try to publish a local struct event without waiting.
    /// </summary>
    bool TryEmit<TEvent>(in TEvent evt)
        where TEvent : struct, IStructEvent;

    /// <summary>
    /// Publish a local struct event, awaiting only requested mailbox backpressure.
    /// </summary>
    ValueTask EmitAsync<TEvent>(TEvent evt, CancellationToken ct = default)
        where TEvent : struct, IStructEvent;

    /// <summary>
    /// Create a caller-owned typed struct inbox.
    /// </summary>
    StructInbox<TEvent> CreateInbox<TEvent>(
        StructInboxOptions? options = null)
        where TEvent : struct, IStructEvent;

    /// <summary>
    /// Register a typed struct callback observer processed by a background subscriber pump.
    /// </summary>
    IDisposable Subscribe<TEvent>(Func<TEvent, ValueTask> handler)
        where TEvent : struct, IStructEvent;

    /// <summary>
    /// Create a pre-bound local struct emitter.
    /// </summary>
    StructEmitter<TEvent> CreateEmitter<TEvent>(
        StructEmitterOptions<TEvent>? options = null)
        where TEvent : struct, IStructEvent;
}
