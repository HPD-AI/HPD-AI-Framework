namespace HPD.Events;

public sealed record RequestStartedEvent(
    string RequestId,
    string SourceName,
    string RequestEventType,
    string ExpectedResponseEventType,
    ResponsePolicy ResponsePolicy,
    ResponderTarget? Target,
    RequestVisibility Visibility,
    DateTimeOffset StartedAt) : Event
{
    public override EventChannel Channel { get; init; } = EventChannel.Control;
    public override EventKind Kind { get; init; } = EventKind.Lifecycle;
}

public sealed record RequestResolvedEvent(
    string RequestId,
    string SourceName,
    string RequestEventType,
    string ResponseEventType,
    string? ResponderId,
    string? ResponderGroup,
    DateTimeOffset ResolvedAt) : Event
{
    public override EventChannel Channel { get; init; } = EventChannel.Control;
    public override EventKind Kind { get; init; } = EventKind.Lifecycle;
}

public sealed record RequestExpiredEvent(
    string RequestId,
    string SourceName,
    string RequestEventType,
    TimeSpan Timeout,
    DateTimeOffset ExpiredAt) : Event
{
    public override EventChannel Channel { get; init; } = EventChannel.Control;
    public override EventKind Kind { get; init; } = EventKind.Lifecycle;
}

public sealed record RequestCancelledEvent(
    string RequestId,
    string SourceName,
    string RequestEventType,
    string? Reason,
    DateTimeOffset CancelledAt) : Event
{
    public override EventChannel Channel { get; init; } = EventChannel.Control;
    public override EventKind Kind { get; init; } = EventKind.Lifecycle;
}

public sealed record ResponseRejectedEvent(
    string RequestId,
    string ResponseEventType,
    RespondStatus Status,
    string? Reason,
    string? ResponderId,
    string? ResponderGroup,
    DateTimeOffset RejectedAt) : Event
{
    public override EventChannel Channel { get; init; } = EventChannel.Control;
    public override EventKind Kind { get; init; } = EventKind.Diagnostic;
}
