using HPD.Agent.Audio.Media;

namespace HPD.Agent.Audio.Turns;

public sealed record EndpointEvidenceProjectionV1
{
    public required EndpointEvidenceIdV1 Id { get; init; }

    public required AudioSessionId SessionId { get; init; }

    public AudioTurnId? TurnId { get; init; }

    public required EndpointEvidenceProjectionKindV1 Kind { get; init; }

    public required EndpointEvidenceProjectionSourceV1 Source { get; init; }

    public required DateTimeOffset ObservedAt { get; init; }

    public MediaTimeline? MediaTime { get; init; }

    public required EndpointEvidenceProjectionDetailV1 Detail { get; init; }

    public AudioCorrelation Correlation { get; init; } = AudioCorrelation.Empty;
}

public enum EndpointEvidenceProjectionKindV1
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

public enum EndpointEvidenceProjectionSourceV1
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

public abstract record EndpointEvidenceProjectionDetailV1;

public sealed record TranscriptEvidenceProjectionDetailV1 : EndpointEvidenceProjectionDetailV1
{
    public required string Text { get; init; }

    public float? Confidence { get; init; }

    public bool IsFinal { get; init; }
}

public sealed record InputContentEvidenceProjectionDetailV1 : EndpointEvidenceProjectionDetailV1
{
    public required InputContentRef Content { get; init; }
}

public sealed record TimerEvidenceProjectionDetailV1 : EndpointEvidenceProjectionDetailV1
{
    public required string TimerName { get; init; }

    public required TimeSpan Elapsed { get; init; }
}

public sealed record OutputPlaybackEvidenceProjectionDetailV1 : EndpointEvidenceProjectionDetailV1
{
    public required OutputFlowId OutputFlowId { get; init; }

    public required string State { get; init; }
}

public sealed record ProviderCommitEvidenceProjectionDetailV1 : EndpointEvidenceProjectionDetailV1
{
    public required ProviderItemRef ProviderItem { get; init; }
}

public sealed record ManualInputEvidenceProjectionDetailV1 : EndpointEvidenceProjectionDetailV1
{
    public required string Text { get; init; }
}

public sealed record ControlInputEvidenceProjectionDetailV1 : EndpointEvidenceProjectionDetailV1
{
    public required string ControlKind { get; init; }

    public AudioExtensionData Metadata { get; init; } = AudioExtensionData.Empty;
}

public sealed record UnknownEvidenceProjectionDetailV1 : EndpointEvidenceProjectionDetailV1
{
    public string? Reason { get; init; }
}
