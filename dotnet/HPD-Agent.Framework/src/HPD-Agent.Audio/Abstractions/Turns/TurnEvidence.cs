using HPD.Agent.Audio.Media;

namespace HPD.Agent.Audio.Turns;

public sealed record TurnEvidence
{
    public required TurnEvidenceId Id { get; init; }

    public required AudioSessionId SessionId { get; init; }

    public AudioTurnId? TurnId { get; init; }

    public required TurnEvidenceKind Kind { get; init; }

    public required TurnEvidenceSource Source { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }

    public MediaTimeline? MediaTime { get; init; }

    public required TurnEvidenceDetail Detail { get; init; }

    public AudioCorrelation Correlation { get; init; } = AudioCorrelation.Empty;
}

public enum TurnEvidenceKind
{
    AudioActivityStarted = 0,
    AudioActivityStopped = 1,
    VoiceActivityScore = 2,
    PartialTranscript = 3,
    PreflightTranscript = 4,
    FinalTranscript = 5,
    TranscriptCorrection = 6,
    SemanticEndOfTurn = 7,
    ProviderSpeechStarted = 8,
    ProviderSpeechStopped = 9,
    ProviderInputCommitted = 10,
    ManualUserInput = 11,
    ControlInput = 12,
    InputMediaContent = 13,
    InputMediaTranscribed = 14,
    OutputPlaybackState = 15,
    SilenceTimer = 16,
    SttLatencyTimer = 17,
    MaxTurnTimer = 18,
    FalseInterruptionTimer = 19
}

public enum TurnEvidenceSource
{
    LocalProcessor = 0,
    Provider = 1,
    SpeechToText = 2,
    InputContent = 3,
    ManualInput = 4,
    ControlInput = 5,
    Timer = 6,
    OutputFlow = 7
}

public abstract record TurnEvidenceDetail;

public sealed record TranscriptEvidenceDetail : TurnEvidenceDetail
{
    public required string Text { get; init; }

    public float? Confidence { get; init; }

    public bool IsFinal { get; init; }
}

public sealed record InputContentEvidenceDetail : TurnEvidenceDetail
{
    public required InputContentRef Content { get; init; }
}

public sealed record TimerEvidenceDetail : TurnEvidenceDetail
{
    public required string TimerName { get; init; }

    public required TimeSpan Elapsed { get; init; }
}

public sealed record OutputPlaybackEvidenceDetail : TurnEvidenceDetail
{
    public required OutputFlowId OutputFlowId { get; init; }

    public required string State { get; init; }
}

public sealed record ProviderCommitEvidenceDetail : TurnEvidenceDetail
{
    public required ProviderItemRef ProviderItem { get; init; }
}

public sealed record ManualInputEvidenceDetail : TurnEvidenceDetail
{
    public required string Text { get; init; }
}

public sealed record ControlInputEvidenceDetail : TurnEvidenceDetail
{
    public required string ControlKind { get; init; }

    public AudioExtensionData Metadata { get; init; } = AudioExtensionData.Empty;
}

public sealed record UnknownEvidenceDetail : TurnEvidenceDetail
{
    public string? Reason { get; init; }
}
