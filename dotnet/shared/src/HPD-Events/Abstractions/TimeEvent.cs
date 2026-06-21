namespace HPD.Events;

/// <summary>
/// Event emitted when a timer fires.
/// </summary>
public sealed record TimeEvent : Event
{
    /// <summary>Name of the timer that fired.</summary>
    public required string TimerName { get; init; }

    /// <summary>Time at which the timer fired.</summary>
    public required DateTimeOffset TriggerTime { get; init; }

    /// <inheritdoc />
    public override EventKind Kind => EventKind.Lifecycle;

    /// <inheritdoc />
    public override EventChannel Channel => EventChannel.Synchronous;
}
