namespace HPD.Events;

/// <summary>
/// Emitted when an event is dropped due to event flow interruption.
/// Universal diagnostic event across all domains (Agent, Graph, etc.).
/// </summary>
public record EventDroppedEvent(
    string DroppedEventFlowId,
    string DroppedEventType,
    long DroppedSequenceNumber
) : Event
{
    /// <inheritdoc />
    public override EventChannel Channel => EventChannel.Control;

    /// <inheritdoc />
    public override EventKind Kind => EventKind.Diagnostic;
}
