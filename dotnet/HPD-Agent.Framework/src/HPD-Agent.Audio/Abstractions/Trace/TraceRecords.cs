using HPD.Agent.Audio.Interaction;
using HPD.Agent.Audio.Interruptions;
using HPD.Agent.Audio.Ledger;
using HPD.Agent.Audio.Media;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Policies;
using HPD.Agent.Audio.Providers;
using HPD.Agent.Audio.Transports;
using HPD.Agent.Audio.Turns;

namespace HPD.Agent.Audio.Trace;

public sealed record AudioInputContentTraceRecord : RealtimeAudioTraceRecord
{
    public required InputContentRef Content { get; init; }

    public required InputMediaDisposition Disposition { get; init; }
}

public sealed record AudioTransportEventTraceRecord : RealtimeAudioTraceRecord
{
    public required TransportEvent Event { get; init; }
}

public sealed record AudioPolicyTraceRecord : RealtimeAudioTraceRecord
{
    public required AudioPolicySet PolicySet { get; init; }
}

public sealed record AudioRouteTraceRecord : RealtimeAudioTraceRecord
{
    public required ProviderRouteDecision Decision { get; init; }
}

public sealed record AudioInteractionUpdateTraceRecord : RealtimeAudioTraceRecord
{
    public required AudioInteractionUpdate Update { get; init; }
}

public sealed record AudioTurnDecisionTraceRecord : RealtimeAudioTraceRecord
{
    public required TurnDecision Decision { get; init; }
}

public sealed record AudioLedgerTraceRecord : RealtimeAudioTraceRecord
{
    public required LedgerRecordId LedgerRecordId { get; init; }

    public required LedgerRecordFamily LedgerFamily { get; init; }
}

public sealed record AudioThreadProjectionTraceRecord : RealtimeAudioTraceRecord
{
    public required ThreadProjectionId ProjectionId { get; init; }

    public ThreadProjectedEventRef? ProjectedEvent { get; init; }
}

public sealed record AudioAssistantOutputTraceRecord : RealtimeAudioTraceRecord
{
    public required OutputFlowId OutputFlowId { get; init; }

    public required ResponseId ResponseId { get; init; }

    public required OutputDisposition Disposition { get; init; }

    public string Text { get; init; } = string.Empty;
}

public sealed record AudioTtsSynthesisTraceRecord : RealtimeAudioTraceRecord
{
    public required OutputFlowId OutputFlowId { get; init; }

    public required ResponseId ResponseId { get; init; }

    public required TtsSynthesisDisposition Disposition { get; init; }

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

    public string? MediaType { get; init; }

    public long? SizeBytes { get; init; }

    public TimeSpan? Duration { get; init; }

    public DateTimeOffset? ProviderFirstAudioAt { get; init; }

    public AudioErrorInfo? Error { get; init; }
}

public sealed record AudioOutputArtifactTraceRecord : RealtimeAudioTraceRecord
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

    public string? MediaType { get; init; }

    public long? SizeBytes { get; init; }

    public string? Sha256 { get; init; }
}

public sealed record AudioOutputPlaybackTraceRecord : RealtimeAudioTraceRecord
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

public sealed record AudioStructEventSampleTraceRecord : RealtimeAudioTraceRecord
{
    public required string StructEventType { get; init; }

    public required long SequenceNumber { get; init; }

    public OutputFlowId? OutputFlowId { get; init; }

    public OutputSegmentId? SegmentId { get; init; }

    public int SegmentIndex { get; init; }
}

public sealed record AudioInterruptionRepairTraceRecord : RealtimeAudioTraceRecord
{
    public required InterruptionRepairRecord Repair { get; init; }
}

public sealed record AudioClockTraceRecord : RealtimeAudioTraceRecord
{
    public required string ClockName { get; init; }

    public required DateTimeOffset Now { get; init; }
}

public sealed record AudioErrorTraceRecord : RealtimeAudioTraceRecord
{
    public required AudioErrorInfo Error { get; init; }
}
