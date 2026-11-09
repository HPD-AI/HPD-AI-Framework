using HPD.Agent.Audio.Media;

namespace HPD.Agent.Audio.Turns;

public sealed record EndpointEvidenceProjection
{
    public required EndpointEvidenceId Id { get; init; }

    public required AudioSessionId SessionId { get; init; }

    public AudioTurnId? TurnId { get; init; }

    public required EndpointEvidenceProjectionKind Kind { get; init; }

    public required EndpointEvidenceProjectionSource Source { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }

    public MediaTimeline? MediaTime { get; init; }

    public required EndpointEvidenceProjectionDetail Detail { get; init; }

    public AudioCorrelation Correlation { get; init; } = AudioCorrelation.Empty;
}

public enum EndpointEvidenceProjectionKind
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

public enum EndpointEvidenceProjectionSource
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

public abstract record EndpointEvidenceProjectionDetail;

public sealed record TranscriptEvidenceProjectionDetail : EndpointEvidenceProjectionDetail
{
    public required string Text { get; init; }

    public float? Confidence { get; init; }

    public bool IsFinal { get; init; }
}

public sealed record InputContentEvidenceProjectionDetail : EndpointEvidenceProjectionDetail
{
    public required InputContentRef Content { get; init; }
}

public sealed record TimerEvidenceProjectionDetail : EndpointEvidenceProjectionDetail
{
    public required string TimerName { get; init; }

    public required TimeSpan Elapsed { get; init; }
}

public sealed record OutputPlaybackEvidenceProjectionDetail : EndpointEvidenceProjectionDetail
{
    public required OutputFlowId OutputFlowId { get; init; }

    public required string State { get; init; }
}

public sealed record ProviderCommitEvidenceProjectionDetail : EndpointEvidenceProjectionDetail
{
    public required ProviderItemRef ProviderItem { get; init; }
}

public sealed record ManualInputEvidenceProjectionDetail : EndpointEvidenceProjectionDetail
{
    public required string Text { get; init; }
}

public sealed record ControlInputEvidenceProjectionDetail : EndpointEvidenceProjectionDetail
{
    public required string ControlKind { get; init; }

    public AudioExtensionData Metadata { get; init; } = AudioExtensionData.Empty;
}

public sealed record UnknownEvidenceProjectionDetail : EndpointEvidenceProjectionDetail
{
    public string? Reason { get; init; }
}
