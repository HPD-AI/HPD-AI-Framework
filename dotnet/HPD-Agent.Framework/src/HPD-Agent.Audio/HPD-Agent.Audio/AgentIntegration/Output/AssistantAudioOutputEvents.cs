using HPD.Agent;
using HPD.Agent.Audio.Ledger;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;
using HPD.Agent.Serialization;

namespace HPD.Agent.Audio.AgentIntegration.Output;

[EventDurability(AgentEventDurability.Durable)]
public abstract record AssistantAudioEvent : AgentEvent
{
    protected AssistantAudioEvent(string sessionId)
    {
        SessionId = sessionId;
    }
}

public sealed record AssistantAudioOutputStartedEvent(
    string SessionId,
    string OutputFlowId,
    string ResponseId,
    string ProviderKey,
    string? ModelId,
    string? VoiceId,
    string? Language,
    string? OutputFormat) : AssistantAudioEvent(SessionId), IObservabilityEvent;

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

public sealed record AssistantAudioPushTextInputSentEvent(
    string SessionId,
    string OutputFlowId,
    string ResponseId,
    int SourceTextStart,
    int SourceTextLength,
    bool IsFinalInput,
    string InputAggregationMode) : AssistantAudioEvent(SessionId), IObservabilityEvent;

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

public sealed record AssistantAudioOutputCompletedEvent(
    string SessionId,
    string OutputFlowId,
    string ResponseId,
    string Disposition,
    int SegmentCount,
    bool Played,
    bool HeardByUser) : AssistantAudioEvent(SessionId), IObservabilityEvent;

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

public sealed record AssistantAudioPlaybackStartedEvent(
    string SessionId,
    string OutputFlowId,
    string ResponseId,
    string? SegmentId,
    int SegmentSequence,
    string MediaType) : AssistantAudioEvent(SessionId), IObservabilityEvent;

public sealed record AssistantAudioPlaybackQueuedEvent(
    string SessionId,
    string OutputFlowId,
    string ResponseId,
    string SegmentId,
    int SegmentSequence,
    string MediaType,
    bool Played,
    bool HeardByUser) : AssistantAudioEvent(SessionId), IObservabilityEvent;

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

public sealed record AssistantAudioPlaybackFailedEvent(
    string SessionId,
    string OutputFlowId,
    string ResponseId,
    string? SegmentId,
    int SegmentSequence,
    AudioErrorInfo Error,
    bool Played,
    bool HeardByUser) : AssistantAudioEvent(SessionId), IObservabilityEvent;
