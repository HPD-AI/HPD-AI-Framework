using HPD.Agent.Audio.Ledger;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Trace;

namespace HPD.Agent.Audio.AgentIntegration.Output;

public sealed record AssistantTextToSpeechOutputResult
{
    public required AudioSessionId SessionId { get; init; }

    public required OutputFlowId OutputFlowId { get; init; }

    public required ResponseId ResponseId { get; init; }

    public required AssistantTextToSpeechOutputStatus Status { get; init; }

    public OutputSegmentId? SegmentId { get; init; }

    public int? SegmentIndex { get; init; }

    public string Text { get; init; } = string.Empty;

    public OutputCommitRecord? Commit { get; init; }

    public string? MediaType { get; init; }

    public AudioErrorInfo? Error { get; init; }

    public required IReadOnlyList<RealtimeLedgerRecord> Ledger { get; init; }

    public required IReadOnlyList<RealtimeAudioTraceRecord> Trace { get; init; }
}

public enum AssistantTextToSpeechOutputStatus
{
    Disabled = 0,
    SkippedNoText = 1,
    TextOnly = 2,
    SynthesizedNotPlayed = 3,
    SynthesisFailedTextOnly = 4
}
