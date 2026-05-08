namespace HPD.Events;

/// <summary>
/// Emitted when a handler subscription faults and is disabled.
/// </summary>
public sealed record EventSubscriberFaultedEvent(
    string SubscriberId,
    string EventType,
    string ErrorType,
    string ErrorMessage) : Event
{
    /// <inheritdoc />
    public override EventChannel Channel => EventChannel.Control;

    /// <inheritdoc />
    public override EventKind Kind => EventKind.Diagnostic;
}
