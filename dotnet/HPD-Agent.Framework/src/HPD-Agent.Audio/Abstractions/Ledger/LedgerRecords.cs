using HPD.Agent.Audio.Media;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Policies;
using HPD.Agent.Audio.Turns;
using HPD.Agent.Audio.Interruptions;

namespace HPD.Agent.Audio.Ledger;

public sealed record InputContentLedgerRecord : RealtimeLedgerRecord
{
    public required InputContentRef Content { get; init; }

    public required InputMediaDisposition Disposition { get; init; }

    public string? Reason { get; init; }
}

public sealed record TranscriptLedgerRecord : RealtimeLedgerRecord
{
    public required AudioTurnId TurnId { get; init; }

    public required string Text { get; init; }

    public required bool IsFinal { get; init; }

    public InputContentId? InputContentId { get; init; }
}

public sealed record UserTurnLedgerRecord : RealtimeLedgerRecord
{
    public required AudioTurnId TurnId { get; init; }

    public required string Text { get; init; }

    public required IReadOnlyList<EndpointEvidenceIdV1> EvidenceIds { get; init; }

    public required EndpointCommitProjectionReasonV1 CommitReason { get; init; }
}

public sealed record AssistantOutputLedgerRecord : RealtimeLedgerRecord
{
    public required OutputFlowId OutputFlowId { get; init; }

    public required ResponseId ResponseId { get; init; }

    public required string Text { get; init; }

    public required OutputDisposition Disposition { get; init; }
}

public sealed record TtsSynthesisRequestedLedgerRecord : RealtimeLedgerRecord
{
    public required OutputFlowId OutputFlowId { get; init; }

    public required ResponseId ResponseId { get; init; }

    public required string Text { get; init; }

    public OutputSegmentId? SegmentId { get; init; }

    public int SegmentIndex { get; init; }

    public bool IsFinalSegment { get; init; } = true;

    public int SourceTextStart { get; init; }

    public int SourceTextLength { get; init; }

    public required string ProviderKey { get; init; }

    public string? ModelId { get; init; }

    public string? VoiceId { get; init; }

    public string? Language { get; init; }

    public string? OutputFormat { get; init; }

    public string? ContentType { get; init; }
}

public sealed record TtsSynthesisResultLedgerRecord : RealtimeLedgerRecord
{
    public required OutputFlowId OutputFlowId { get; init; }

    public required ResponseId ResponseId { get; init; }

    public required TtsSynthesisDisposition Disposition { get; init; }

    public OutputSegmentId? SegmentId { get; init; }

    public int SegmentIndex { get; init; }

    public bool IsFinalSegment { get; init; } = true;

    public int SourceTextStart { get; init; }

    public int SourceTextLength { get; init; }

    public string? MediaType { get; init; }

    public long? SizeBytes { get; init; }

    public TimeSpan? Duration { get; init; }

    public AudioErrorInfo? Error { get; init; }
}

public sealed record OutputArtifactLedgerRecord : RealtimeLedgerRecord
{
    public required OutputFlowId OutputFlowId { get; init; }

    public required ResponseId ResponseId { get; init; }

    public required AudioArtifactRef Artifact { get; init; }

    public OutputSegmentId? SegmentId { get; init; }

    public int SegmentIndex { get; init; }

    public bool IsFinalSegment { get; init; } = true;

    public int SourceTextStart { get; init; }

    public int SourceTextLength { get; init; }

    public required OutputArtifactKind Kind { get; init; }

    public MediaCaptureDisposition CaptureDisposition { get; init; } = MediaCaptureDisposition.ArtifactRef;
}

public sealed record OutputPlaybackLedgerRecord : RealtimeLedgerRecord
{
    public required OutputFlowId OutputFlowId { get; init; }

    public required ResponseId ResponseId { get; init; }

    public OutputSegmentId? SegmentId { get; init; }

    public int SegmentIndex { get; init; }

    public required OutputPlaybackDisposition Disposition { get; init; }

    public TimeSpan PlayedDuration { get; init; }

    public int PlayedTextLength { get; init; }

    public OutputAlignmentPrecision Precision { get; init; } = OutputAlignmentPrecision.Unknown;

    public AudioErrorInfo? Error { get; init; }
}

public sealed record ThreadProjectionLedgerRecord : RealtimeLedgerRecord
{
    public required ThreadProjectionId ProjectionId { get; init; }

    public required ThreadRef Thread { get; init; }

    public required ThreadProjectionRecord Projection { get; init; }

    public ThreadProjectedEventRef? ProjectedEvent { get; init; }
}

public sealed record InterruptionRepairLedgerRecord : RealtimeLedgerRecord
{
    public required InterruptionRepairRecord Repair { get; init; }
}

public enum OutputDisposition
{
    Draft = 0,
    Interrupted = 2,
    Canceled = 3,
    Failed = 4,
    TextOnly = 5,
    SynthesizedNotPlayed = 7,
    SynthesisFailedTextOnly = 8,
    SegmentSynthesized = 9,
    SegmentFailedTextOnly = 10,
    QueuedUnplayed = 11,
    PlayedPartial = 12,
    PlayedComplete = 13,
    PlaybackFailed = 14
}

public enum TtsSynthesisDisposition
{
    Requested = 0,
    Synthesized = 1,
    Failed = 2,
    SkippedByPolicy = 3,
    Unsupported = 4
}

public enum OutputArtifactKind
{
    SynthesizedAudio = 0,
    AlignmentMetadata = 1
}

public enum OutputPlaybackDisposition
{
    Queued = 0,
    Started = 1,
    Progress = 2,
    PlayedComplete = 3,
    Interrupted = 4,
    PlaybackFailed = 5,
    QueuedUnplayed = 6
}

public sealed record ThreadProjectionRecord
{
    public required AudioTurnId TurnId { get; init; }

    public required string Text { get; init; }

    public ThreadProjectionKind Kind { get; init; } = ThreadProjectionKind.UserTurn;

    public ThreadProjectionRole Role { get; init; } = ThreadProjectionRole.User;

    public InputContentId? InputContentId { get; init; }

    public OutputFlowId? OutputFlowId { get; init; }

    public ResponseId? ResponseId { get; init; }
}

public enum ThreadProjectionKind
{
    UserTurn = 0,
    AssistantOutput = 1
}

public enum ThreadProjectionRole
{
    User = 0,
    Assistant = 1
}

public interface IThreadProjectionSink
{
    ValueTask<ThreadProjectedEventRef> ProjectAsync(
        ThreadRef thread,
        ThreadProjectionRecord record,
        CancellationToken cancellationToken = default);
}
