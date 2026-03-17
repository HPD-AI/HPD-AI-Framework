namespace HPD.Events;

/// <summary>
/// Emitted when an event is dropped due to stream interruption.
/// Universal diagnostic event across all domains (Agent, Graph, etc.).
/// </summary>
public record EventDroppedEvent(
    string DroppedStreamId,
    string DroppedEventType,
    long DroppedSequenceNumber
) : Event
{
    public new EventKind Kind { get; init; } = EventKind.Diagnostic;
}
