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
    /// <inheritdoc />
    public override EventChannel Channel { get; init; } = EventChannel.Control;

    /// <inheritdoc />
    public override EventKind Kind { get; init; } = EventKind.Diagnostic;
}
