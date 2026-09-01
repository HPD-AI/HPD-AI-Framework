using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Media;
using HPD.Audio.Primitives;

namespace HPD.Agent.Audio.Output;

public abstract record OutputSegment
{
    public required OutputSegmentId Id { get; init; }

    public required ResponseId ResponseId { get; init; }
}

public sealed record OutputTextSegment : OutputSegment
{
    public required string Text { get; init; }
}

public sealed record OutputAudioSegment : OutputSegment
{
    public required MediaPayloadRef Payload { get; init; }

    public required MediaFormatDescriptor Format { get; init; }

    public int SegmentIndex { get; init; }

    public bool IsFinalSegment { get; init; }

    public int SourceTextStart { get; init; }

    public int SourceTextLength { get; init; }

    public TextToSpeechAlignment? Alignment { get; init; }
}

public sealed record OutputFlowSnapshot
{
    public required OutputFlowId Id { get; init; }

    public required OutputFlowState State { get; init; }

    public ResponseId? ResponseId { get; init; }

    public IReadOnlyList<OutputSegmentId> SegmentIds { get; init; } = [];

    public string Text { get; init; } = string.Empty;

    public IReadOnlyList<OutputAudioStream> AudioStreams { get; init; } = [];

    public IReadOnlyList<OutputAudioChunkMetadata> AudioChunks { get; init; } = [];

    public IReadOnlyList<OutputAudioArtifact> AudioArtifacts { get; init; } = [];

    public OutputPlaybackBoundary? PlaybackBoundary { get; init; }
}

public sealed record OutputCommitRecord
{
    public required OutputFlowId OutputFlowId { get; init; }

    public required ResponseId ResponseId { get; init; }

    public required OutputCommitDisposition Disposition { get; init; }

    public string Text { get; init; } = string.Empty;

    public IReadOnlyList<OutputAudioStream> AudioStreams { get; init; } = [];

    public IReadOnlyList<OutputAudioArtifact> AudioArtifacts { get; init; } = [];

    public OutputPlaybackBoundary? PlaybackBoundary { get; init; }

    public string? Reason { get; init; }
}

public sealed record OutputAudioStream
{
    /// <summary>The owning Agent session used to route live playback.</summary>
    public string? SessionId { get; init; }

    public required OutputFlowId OutputFlowId { get; init; }

    public required ResponseId ResponseId { get; init; }

    public required OutputSegmentId SegmentId { get; init; }

    public int SegmentIndex { get; init; }

    public bool IsFinalSegment { get; init; }

    public int SourceTextStart { get; init; }

    public int SourceTextLength { get; init; }

    public string? ProviderKey { get; init; }

    public string? ModelId { get; init; }

    public string? VoiceId { get; init; }

    public string? Language { get; init; }

    public string? OutputFormat { get; init; }

    public required string MediaType { get; init; }

    public OutputAudioPayloadKind PayloadKind { get; init; }

    public DateTimeOffset StartedAt { get; init; }

    public TextToSpeechAlignment? Alignment { get; init; }

    public OutputInterruptibility Interruptibility { get; init; } = OutputInterruptibility.Interruptible;
}

public enum OutputAudioPayloadKind
{
    DecodedPcmFrame = 1,
    EncodedBytes = 2
}

public abstract record OutputAudioPayload
{
    public abstract OutputAudioPayloadKind Kind { get; }

    public abstract string MediaType { get; }

    public abstract long SizeBytes { get; }

    public abstract TimeSpan? Duration { get; }
}

public sealed record DecodedOutputAudioFrame : OutputAudioPayload
{
    public required HPD.Audio.Primitives.AudioFrame Frame { get; init; }

    public override OutputAudioPayloadKind Kind => OutputAudioPayloadKind.DecodedPcmFrame;

    public override string MediaType => "audio/pcm";

    public override long SizeBytes => Frame.Data.Length;

    public override TimeSpan? Duration => Frame.Duration;
}

public sealed record EncodedOutputAudioData : OutputAudioPayload
{
    public required string ContentType { get; init; }

    public required ReadOnlyMemory<byte> Data { get; init; }

    public TimeSpan? EstimatedDuration { get; init; }

    public override OutputAudioPayloadKind Kind => OutputAudioPayloadKind.EncodedBytes;

    public override string MediaType => ContentType;

    public override long SizeBytes => Data.Length;

    public override TimeSpan? Duration => EstimatedDuration;
}

public sealed record OutputAudioChunk
{
    public required OutputFlowId OutputFlowId { get; init; }

    public required ResponseId ResponseId { get; init; }

    public required OutputSegmentId SegmentId { get; init; }

    public required int SegmentIndex { get; init; }

    public required int Sequence { get; init; }

    public required OutputAudioPayload Payload { get; init; }

    public string MediaType => Payload.MediaType;

    public long SizeBytes => Payload.SizeBytes;

    public TimeSpan? Duration => Payload.Duration;

    public DateTimeOffset ObservedAt { get; init; }

    public bool IsFinalChunk { get; init; }

    public string? ProviderRequestId { get; init; }
}

public sealed record OutputAudioChunkMetadata
{
    public required OutputFlowId OutputFlowId { get; init; }

    public required ResponseId ResponseId { get; init; }

    public required OutputSegmentId SegmentId { get; init; }

    public required int SegmentIndex { get; init; }

    public required int Sequence { get; init; }

    public required OutputAudioPayloadKind PayloadKind { get; init; }

    public required string MediaType { get; init; }

    public long SizeBytes { get; init; }

    public TimeSpan? Duration { get; init; }

    public DateTimeOffset ObservedAt { get; init; }

    public bool IsFinalChunk { get; init; }
}

public sealed record OutputAudioStreamCompletion
{
    public required OutputFlowId OutputFlowId { get; init; }

    public required ResponseId ResponseId { get; init; }

    public required OutputSegmentId SegmentId { get; init; }

    public required int SegmentIndex { get; init; }

    public required OutputAudioStreamDisposition Disposition { get; init; }

    public int ChunkCount { get; init; }

    public long SizeBytes { get; init; }

    public TimeSpan? Duration { get; init; }

    public DateTimeOffset CompletedAt { get; init; }

    public AudioErrorInfo? Error { get; init; }
}

public sealed record OutputAudioArtifact
{
    public required OutputFlowId OutputFlowId { get; init; }

    public required ResponseId ResponseId { get; init; }

    public required OutputSegmentId SegmentId { get; init; }

    public required int SegmentIndex { get; init; }

    public required AudioArtifactRef Artifact { get; init; }

    public required string MediaType { get; init; }

    public long? SizeBytes { get; init; }

    public string? Sha256 { get; init; }

    public TimeSpan? Duration { get; init; }

    public DateTimeOffset CapturedAt { get; init; }
}

public sealed record OutputPlaybackBoundary
{
    public required OutputFlowId OutputFlowId { get; init; }

    public required ResponseId ResponseId { get; init; }

    public OutputSegmentId? SegmentId { get; init; }

    public int SegmentIndex { get; init; }

    public TimeSpan PlayedDuration { get; init; }

    public required int PlayedTextLength { get; init; }

    public OutputAlignmentPrecision Precision { get; init; } = OutputAlignmentPrecision.Unknown;

    public DateTimeOffset? ObservedAt { get; init; }
}

public sealed record OutputPlaybackRequest
{
    public required OutputFlowId OutputFlowId { get; init; }

    public required ResponseId ResponseId { get; init; }

    public required OutputSegmentId SegmentId { get; init; }

    public required int SegmentIndex { get; init; }

    public TimeSpan? EstimatedDuration { get; init; }

    public string? EventFlowId { get; init; }

    public int SourceTextStart { get; init; }

    public int SourceTextLength { get; init; }

    public bool IsFinalSegment { get; init; }

    public string? MediaType { get; init; }

    public TextToSpeechAlignment? Alignment { get; init; }

    public OutputInterruptibility Interruptibility { get; init; } = OutputInterruptibility.Interruptible;
}

public sealed record OutputSinkStartResult
{
    public required OutputFlowId OutputFlowId { get; init; }

    public required ResponseId ResponseId { get; init; }

    public required OutputSegmentId SegmentId { get; init; }

    public required int SegmentIndex { get; init; }

    public required OutputSinkStartDisposition Disposition { get; init; }

    public AudioErrorInfo? Error { get; init; }
}

public sealed record OutputPlaybackCursor
{
    public required OutputFlowId OutputFlowId { get; init; }

    public required ResponseId ResponseId { get; init; }

    public OutputSegmentId? SegmentId { get; init; }

    public int SegmentIndex { get; init; }

    public TimeSpan PlayedDuration { get; init; }

    public int PlayedTextLength { get; init; }

    public OutputAlignmentPrecision Precision { get; init; } = OutputAlignmentPrecision.Unknown;

    public DateTimeOffset? ObservedAt { get; init; }
}

public sealed record OutputPlaybackFailure
{
    public required OutputFlowId OutputFlowId { get; init; }

    public required ResponseId ResponseId { get; init; }

    public OutputSegmentId? SegmentId { get; init; }

    public int SegmentIndex { get; init; }

    public required AudioErrorInfo Error { get; init; }

    public DateTimeOffset? ObservedAt { get; init; }
}

public abstract record OutputPlaybackEvent
{
    public required OutputFlowId OutputFlowId { get; init; }

    public required ResponseId ResponseId { get; init; }

    public required OutputSegmentId SegmentId { get; init; }

    public required int SegmentIndex { get; init; }

    public DateTimeOffset? ObservedAt { get; init; }
}

public sealed record OutputPlaybackQueuedEvent : OutputPlaybackEvent;

public sealed record OutputPlaybackStartedEvent : OutputPlaybackEvent;

public sealed record OutputPlaybackProgressEvent : OutputPlaybackEvent
{
    public required OutputPlaybackCursor Cursor { get; init; }
}

public sealed record OutputPlaybackCompletedEvent : OutputPlaybackEvent
{
    public required OutputPlaybackCursor Cursor { get; init; }
}

public sealed record OutputPlaybackInterruptedEvent : OutputPlaybackEvent
{
    public required OutputPlaybackBoundary Boundary { get; init; }
}

public sealed record OutputPlaybackClearedEvent : OutputPlaybackEvent
{
    public required OutputPlaybackBoundary Boundary { get; init; }
}

public sealed record OutputPlaybackFailedEvent : OutputPlaybackEvent
{
    public required AudioErrorInfo Error { get; init; }
}

public sealed record TextToSpeechAlignment
{
    public IReadOnlyList<TextToSpeechAlignmentSpan> Spans { get; init; } = [];

    public OutputAlignmentPrecision Precision { get; init; } = OutputAlignmentPrecision.Unknown;
}

public sealed record TextToSpeechAlignmentSpan
{
    public required int SourceTextStart { get; init; }

    public required int SourceTextLength { get; init; }

    public TimeSpan? AudioStart { get; init; }

    public TimeSpan? AudioDuration { get; init; }

    public string? Text { get; init; }
}

public enum OutputAlignmentPrecision
{
    Unknown = 0,
    Exact = 1,
    Approximate = 2,
    LocalOnly = 3
}

public enum OutputFlowState
{
    Created = 0,
    GeneratingText = 1,
    TextReady = 2,
    SynthesizingAudio = 3,
    AudioStreaming = 4,
    AudioStreamCompleted = 5,
    ArtifactCaptured = 6,
    Queued = 7,
    Playing = 8,
    PlayedPartial = 9,
    PlayedComplete = 10,
    Paused = 11,
    Interrupted = 12,
    Truncated = 13,
    Canceled = 14,
    Failed = 15,
    TextOnlyCompleted = 16,
    SynthesizedNotPlayed = 17,
    QueuedUnplayed = 18,
    PlaybackFailed = 19
}

public enum OutputAudioStreamDisposition
{
    Completed = 0,
    Failed = 1,
    Canceled = 2,
    Interrupted = 3
}

public enum OutputCommitDisposition
{
    Interrupted = 1,
    Canceled = 2,
    Failed = 3,
    TextOnly = 4,
    SynthesizedNotPlayed = 5,
    SynthesisFailedTextOnly = 6,
    SegmentSynthesized = 7,
    SegmentFailedTextOnly = 8,
    PlayedPartial = 9,
    PlayedComplete = 10,
    QueuedUnplayed = 11,
    PlaybackFailed = 12
}

public enum OutputSinkStartDisposition
{
    Accepted = 0,
    Rejected = 1,
    Failed = 2
}

public enum OutputInterruptibility
{
    Interruptible = 0,
    Uninterruptible = 1
}
