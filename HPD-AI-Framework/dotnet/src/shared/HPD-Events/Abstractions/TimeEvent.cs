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

    public override EventKind Kind => EventKind.Lifecycle;
    public override EventPriority Priority => EventPriority.Normal;
}
