using HPD.Agent;
using HPD.Agent.Audio.Ledger;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Serialization;

namespace HPD.Agent.Audio.AgentIntegration.Output;

public abstract record AssistantAudioEvent : AgentEvent
{
    protected AssistantAudioEvent(string sessionId)
    {
        SessionId = sessionId;
    }
}

[DurableEvent]
[EventType("ASSISTANT_AUDIO_OUTPUT_STARTED")]
public sealed record AssistantAudioOutputStartedEvent(
    string SessionId,
    string OutputFlowId,
    string ResponseId,
    string ProviderKey,
    string? ModelId,
    string? VoiceId,
    string? Language,
    string? OutputFormat) : AssistantAudioEvent(SessionId), IObservabilityEvent;

[DurableEvent]
[EventType("ASSISTANT_AUDIO_OUTPUT_STREAM_STARTED")]
public sealed record AssistantAudioOutputStreamStartedEvent(
    string SessionId,
    string OutputFlowId,
    string ResponseId,
    string SegmentId,
    int SegmentSequence,
    string ProviderKey,
    string? ModelId,
    string? VoiceId,
    string? Language,
    string? OutputFormat,
    string MediaType,
    string PayloadKind) : AssistantAudioEvent(SessionId), IObservabilityEvent;

[DurableEvent]
[EventType("ASSISTANT_AUDIO_OUTPUT_CHUNK_READY")]
public sealed record AssistantAudioOutputChunkReadyEvent(
    string SessionId,
    string OutputFlowId,
    string ResponseId,
    string SegmentId,
    int SegmentSequence,
    int ChunkSequence,
    string ProviderKey,
    string? ModelId,
    string? VoiceId,
    string? Language,
    string? OutputFormat,
    string MediaType,
    long SizeBytes,
    TimeSpan? Duration,
    bool IsFinalChunk,
    string PayloadKind) : AssistantAudioEvent(SessionId), IObservabilityEvent;

[DurableEvent]
[EventType("ASSISTANT_AUDIO_PUSH_TEXT_STREAM_OPENING")]
public sealed record AssistantAudioPushTextStreamOpeningEvent(
    string SessionId,
    string OutputFlowId,
    string ResponseId,
    string ProviderKey,
    string? ModelId,
    string? VoiceId,
    string? Language,
    string? OutputFormat,
    string InputAggregationMode) : AssistantAudioEvent(SessionId), IObservabilityEvent;

[DurableEvent]
[EventType("ASSISTANT_AUDIO_PUSH_TEXT_STREAM_OPENED")]
public sealed record AssistantAudioPushTextStreamOpenedEvent(
    string SessionId,
    string OutputFlowId,
    string ResponseId,
    string ProviderKey,
    string? ModelId,
    string? VoiceId,
    string? Language,
    string? OutputFormat,
    string InputAggregationMode) : AssistantAudioEvent(SessionId), IObservabilityEvent;

[DurableEvent]
[EventType("ASSISTANT_AUDIO_PUSH_TEXT_INPUT_SENT")]
public sealed record AssistantAudioPushTextInputSentEvent(
    string SessionId,
    string OutputFlowId,
    string ResponseId,
    int SourceTextStart,
    int SourceTextLength,
    bool IsFinalInput,
    string InputAggregationMode) : AssistantAudioEvent(SessionId), IObservabilityEvent;

[DurableEvent]
[EventType("ASSISTANT_AUDIO_OUTPUT_STREAM_COMPLETED")]
public sealed record AssistantAudioOutputStreamCompletedEvent(
    string SessionId,
    string OutputFlowId,
    string ResponseId,
    string SegmentId,
    int SegmentSequence,
    string Disposition,
    int ChunkCount,
    long SizeBytes,
    TimeSpan? Duration) : AssistantAudioEvent(SessionId), IObservabilityEvent;

[DurableEvent]
[EventType("ASSISTANT_AUDIO_OUTPUT_ARTIFACT_CAPTURED")]
public sealed record AssistantAudioOutputArtifactCapturedEvent(
    string SessionId,
    string OutputFlowId,
    string ResponseId,
    string SegmentId,
    int SegmentSequence,
    string MediaType,
    AudioArtifactRef Artifact,
    long? SizeBytes,
    string? Sha256,
    TimeSpan? Duration) : AssistantAudioEvent(SessionId), IObservabilityEvent;

[DurableEvent]
[EventType("ASSISTANT_AUDIO_OUTPUT_SEGMENT_FAILED")]
public sealed record AssistantAudioOutputSegmentFailedEvent(
    string SessionId,
    string OutputFlowId,
    string ResponseId,
    string SegmentId,
    int SegmentSequence,
    string ProviderKey,
    string? ModelId,
    string? VoiceId,
    string? Language,
    string? OutputFormat,
    AudioErrorInfo? Error,
    string Disposition,
    bool IsFinal) : AssistantAudioEvent(SessionId), IObservabilityEvent;

[DurableEvent]
[EventType("ASSISTANT_AUDIO_OUTPUT_COMPLETED")]
public sealed record AssistantAudioOutputCompletedEvent(
    string SessionId,
    string OutputFlowId,
    string ResponseId,
    string Disposition,
    int SegmentCount,
    bool Played,
    bool HeardByUser) : AssistantAudioEvent(SessionId), IObservabilityEvent;

[DurableEvent]
[EventType("ASSISTANT_AUDIO_OUTPUT_FAILED")]
public sealed record AssistantAudioOutputFailedEvent(
    string SessionId,
    string OutputFlowId,
    string ResponseId,
    string ProviderKey,
    string? ModelId,
    string? VoiceId,
    string? Language,
    string? OutputFormat,
    AudioErrorInfo? Error,
    string Disposition) : AssistantAudioEvent(SessionId), IObservabilityEvent;

[DurableEvent]
[EventType("ASSISTANT_AUDIO_PLAYBACK_STARTED")]
public sealed record AssistantAudioPlaybackStartedEvent(
    string SessionId,
    string OutputFlowId,
    string ResponseId,
    string? SegmentId,
    int SegmentSequence,
    string MediaType) : AssistantAudioEvent(SessionId), IObservabilityEvent;

[DurableEvent]
[EventType("ASSISTANT_AUDIO_PLAYBACK_QUEUED")]
public sealed record AssistantAudioPlaybackQueuedEvent(
    string SessionId,
    string OutputFlowId,
    string ResponseId,
    string SegmentId,
    int SegmentSequence,
    string MediaType,
    bool Played,
    bool HeardByUser) : AssistantAudioEvent(SessionId), IObservabilityEvent;

[DurableEvent]
[EventType("ASSISTANT_AUDIO_PLAYBACK_PROGRESS")]
public sealed record AssistantAudioPlaybackProgressEvent(
    string SessionId,
    string OutputFlowId,
    string ResponseId,
    string? SegmentId,
    int SegmentSequence,
    TimeSpan PlayedDuration,
    int PlayedTextLength,
    string Precision,
    bool Played,
    bool HeardByUser) : AssistantAudioEvent(SessionId), IObservabilityEvent;

[DurableEvent]
[EventType("ASSISTANT_AUDIO_PLAYBACK_COMPLETED")]
public sealed record AssistantAudioPlaybackCompletedEvent(
    string SessionId,
    string OutputFlowId,
    string ResponseId,
    string? SegmentId,
    int SegmentSequence,
    string MediaType,
    bool Played,
    bool HeardByUser,
    TimeSpan Duration,
    int PlayedTextLength,
    string Precision) : AssistantAudioEvent(SessionId), IObservabilityEvent;

[DurableEvent]
[EventType("ASSISTANT_AUDIO_PLAYBACK_INTERRUPTED")]
public sealed record AssistantAudioPlaybackInterruptedEvent(
    string SessionId,
    string OutputFlowId,
    string ResponseId,
    string? SegmentId,
    int SegmentSequence,
    TimeSpan PlayedDuration,
    int PlayedTextLength,
    string Precision,
    bool Played,
    bool HeardByUser) : AssistantAudioEvent(SessionId), IObservabilityEvent;

[DurableEvent]
[EventType("ASSISTANT_AUDIO_PLAYBACK_FAILED")]
public sealed record AssistantAudioPlaybackFailedEvent(
    string SessionId,
    string OutputFlowId,
    string ResponseId,
    string? SegmentId,
    int SegmentSequence,
    AudioErrorInfo Error,
    bool Played,
    bool HeardByUser) : AssistantAudioEvent(SessionId), IObservabilityEvent;
