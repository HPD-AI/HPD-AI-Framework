namespace HPD.Agent.Audio.Media;

public sealed record MediaTimeline
{
    public TimeSpan? Offset { get; init; }

    public TimeSpan? Duration { get; init; }

    public DateTimeOffset? CapturedAt { get; init; }

    public long? SequenceNumber { get; init; }
}
