namespace HPD.Events;

/// <summary>
/// Event publishing surface for components that only emit class events.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publish an event to matching subscriber mailboxes without waiting for handler completion.
    /// </summary>
    void Emit(Event evt);

    /// <summary>
    /// Publish an event to matching subscriber mailboxes, awaiting only requested mailbox backpressure.
    /// </summary>
    ValueTask EmitAsync(Event evt, CancellationToken ct = default);
}
