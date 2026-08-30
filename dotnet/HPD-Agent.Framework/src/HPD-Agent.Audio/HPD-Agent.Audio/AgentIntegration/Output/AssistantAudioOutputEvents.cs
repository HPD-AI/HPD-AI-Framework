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

[EventType("ASSISTANT_AUDIO_OUTPUT_STARTED", Durability = AgentEventDurability.Durable)]
public sealed record AssistantAudioOutputStartedEvent(
    string SessionId,
    string OutputFlowId,
    string ResponseId,
    string ProviderKey,
    string? ModelId,
    string? VoiceId,
    string? Language,
    string? OutputFormat) : AssistantAudioEvent(SessionId), IObservabilityEvent;

[EventType("ASSISTANT_AUDIO_OUTPUT_STREAM_STARTED", Durability = AgentEventDurability.Durable)]
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

[EventType("ASSISTANT_AUDIO_OUTPUT_CHUNK_READY", Durability = AgentEventDurability.Durable)]
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

[EventType("ASSISTANT_AUDIO_PUSH_TEXT_STREAM_OPENING", Durability = AgentEventDurability.Durable)]
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

[EventType("ASSISTANT_AUDIO_PUSH_TEXT_STREAM_OPENED", Durability = AgentEventDurability.Durable)]
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

[EventType("ASSISTANT_AUDIO_PUSH_TEXT_INPUT_SENT", Durability = AgentEventDurability.Durable)]
public sealed record AssistantAudioPushTextInputSentEvent(
    string SessionId,
    string OutputFlowId,
    string ResponseId,
    int SourceTextStart,
    int SourceTextLength,
    bool IsFinalInput,
    string InputAggregationMode) : AssistantAudioEvent(SessionId), IObservabilityEvent;

[EventType("ASSISTANT_AUDIO_OUTPUT_STREAM_COMPLETED", Durability = AgentEventDurability.Durable)]
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

[EventType("ASSISTANT_AUDIO_OUTPUT_ARTIFACT_CAPTURED", Durability = AgentEventDurability.Durable)]
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

[EventType("ASSISTANT_AUDIO_OUTPUT_SEGMENT_FAILED", Durability = AgentEventDurability.Durable)]
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

[EventType("ASSISTANT_AUDIO_OUTPUT_COMPLETED", Durability = AgentEventDurability.Durable)]
public sealed record AssistantAudioOutputCompletedEvent(
    string SessionId,
    string OutputFlowId,
    string ResponseId,
    string Disposition,
    int SegmentCount,
    bool Played,
    bool HeardByUser) : AssistantAudioEvent(SessionId), IObservabilityEvent;

[EventType("ASSISTANT_AUDIO_OUTPUT_FAILED", Durability = AgentEventDurability.Durable)]
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

[EventType("ASSISTANT_AUDIO_PLAYBACK_STARTED", Durability = AgentEventDurability.Durable)]
public sealed record AssistantAudioPlaybackStartedEvent(
    string SessionId,
    string OutputFlowId,
    string ResponseId,
    string? SegmentId,
    int SegmentSequence,
    string MediaType) : AssistantAudioEvent(SessionId), IObservabilityEvent;

[EventType("ASSISTANT_AUDIO_PLAYBACK_QUEUED", Durability = AgentEventDurability.Durable)]
public sealed record AssistantAudioPlaybackQueuedEvent(
    string SessionId,
    string OutputFlowId,
    string ResponseId,
    string SegmentId,
    int SegmentSequence,
    string MediaType,
    bool Played,
    bool HeardByUser) : AssistantAudioEvent(SessionId), IObservabilityEvent;

[EventType("ASSISTANT_AUDIO_PLAYBACK_PROGRESS", Durability = AgentEventDurability.Durable)]
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

[EventType("ASSISTANT_AUDIO_PLAYBACK_COMPLETED", Durability = AgentEventDurability.Durable)]
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

[EventType("ASSISTANT_AUDIO_PLAYBACK_INTERRUPTED", Durability = AgentEventDurability.Durable)]
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

[EventType("ASSISTANT_AUDIO_PLAYBACK_FAILED", Durability = AgentEventDurability.Durable)]
public sealed record AssistantAudioPlaybackFailedEvent(
    string SessionId,
    string OutputFlowId,
    string ResponseId,
    string? SegmentId,
    int SegmentSequence,
    AudioErrorInfo Error,
    bool Played,
    bool HeardByUser) : AssistantAudioEvent(SessionId), IObservabilityEvent;
