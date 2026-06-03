namespace HPD.Agent.Audio.Interaction;

using HPD.Agent.Audio.Media;

public abstract record AudioInteractionUpdate
{
    public required InteractionSessionId SessionId { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }

    public required ProviderRouteEpochId RouteEpochId { get; init; }

    public AudioCorrelation Correlation { get; init; } = AudioCorrelation.Empty;
}

public sealed record TranscriptUpdate : AudioInteractionUpdate
{
    public required TranscriptStage Stage { get; init; }

    public required string Text { get; init; }

    public float? Confidence { get; init; }

    public InputContentId? InputContentId { get; init; }
}

public sealed record ResponseLifecycleUpdate : AudioInteractionUpdate
{
    public required ResponseId ResponseId { get; init; }

    public required ResponseLifecycleState State { get; init; }
}

public sealed record OutputTextUpdate : AudioInteractionUpdate
{
    public required ResponseId ResponseId { get; init; }

    public required string Delta { get; init; }

    public bool IsFinal { get; init; }
}

public sealed record OutputAudioUpdate : AudioInteractionUpdate
{
    public required ResponseId ResponseId { get; init; }

    public required CanonicalMediaEnvelope Audio { get; init; }
}

public sealed record ToolCallUpdate : AudioInteractionUpdate
{
    public required string ToolCallId { get; init; }

    public required string Name { get; init; }

    public string? ArgumentsDelta { get; init; }

    public bool IsFinal { get; init; }
}

public sealed record ProviderErrorUpdate : AudioInteractionUpdate
{
    public required AudioErrorInfo Error { get; init; }
}

public sealed record ProviderRepairUpdate : AudioInteractionUpdate
{
    public required ProviderRepairResult Result { get; init; }
}

public enum TranscriptStage
{
    Partial = 0,
    Preflight = 1,
    Final = 2,
    Correction = 3
}

public enum ResponseLifecycleState
{
    Created = 0,
    InProgress = 1,
    Completed = 2,
    Incomplete = 3,
    Cancelled = 4,
    Failed = 5
}
