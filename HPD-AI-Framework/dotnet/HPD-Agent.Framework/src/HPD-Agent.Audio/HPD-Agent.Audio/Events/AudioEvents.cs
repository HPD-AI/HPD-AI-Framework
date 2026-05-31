// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using HPD.Events;
using HPD.Events.Struct;
using HPD.Agent.Audio.Preemptive;

namespace HPD.Agent.Audio;

//
// SYNTHESIS EVENTS
//

/// <summary>
/// Emitted when TTS synthesis begins for a response.
/// </summary>
public record SynthesisStartedEvent(
    string SynthesisId,
    string? ModelId,
    string? Voice
) : AgentEvent;

/// <summary>
/// Emitted for each audio chunk during streaming synthesis.
/// Primary event for delivering audio to clients.
/// </summary>
public record AudioChunkEvent(
    string SynthesisId,
    string Base64Audio,
    string MimeType,
    int ChunkIndex,
    TimeSpan Duration,
    bool IsLast
) : AgentEvent;

/// <summary>
/// Local zero-allocation audio output frame for hot-path audio delivery.
/// </summary>
public readonly record struct AudioOutputFrame(
    string OutputId,
    ReadOnlyMemory<byte> Audio,
    string MimeType,
    int ChunkIndex,
    TimeSpan Duration,
    bool IsLast,
    long TimestampNs = 0,
    long SequenceNumber = 0
) : IStructEvent, ISequencedStructEvent<AudioOutputFrame>
{
    /// <inheritdoc />
    public EventKind Kind => EventKind.Content;

    /// <inheritdoc />
    public AudioOutputFrame WithSequenceNumber(long sequenceNumber) =>
        this with { SequenceNumber = sequenceNumber };
}

/// <summary>
/// Local audio input frame for runtime voice capture.
/// </summary>
public readonly record struct AudioInputFrame(
    string? SessionId,
    string? BranchId,
    ReadOnlyMemory<byte> Audio,
    string MimeType,
    long TimestampNs,
    bool IsFinal,
    long SequenceNumber = 0
) : IStructEvent, ISequencedStructEvent<AudioInputFrame>
{
    /// <inheritdoc />
    public EventKind Kind => EventKind.Content;

    /// <inheritdoc />
    public AudioInputFrame WithSequenceNumber(long sequenceNumber) =>
        this with { SequenceNumber = sequenceNumber };
}

/// <summary>
/// Emitted when TTS synthesis completes.
/// </summary>
public record SynthesisCompletedEvent(
    string SynthesisId,
    bool WasInterrupted = false,
    int TotalChunks = 0,
    int DeliveredChunks = 0
) : AgentEvent;

//
// TRANSCRIPTION EVENTS
//

/// <summary>
/// Emitted for streaming transcription updates.
/// </summary>
public record TranscriptionDeltaEvent(
    string TranscriptionId,
    string Text,
    bool IsFinal,
    float? Confidence
) : AgentEvent;

/// <summary>
/// Emitted when transcription completes.
/// </summary>
public record TranscriptionCompletedEvent(
    string TranscriptionId,
    string FinalText,
    TimeSpan ProcessingDuration
) : AgentEvent;

//
// INTERRUPTION EVENTS
//

/// <summary>
/// Emitted when user interrupts bot speech.
/// </summary>
public record UserInterruptedEvent(
    string? TranscribedText
) : AgentEvent;

/// <summary>
/// Emitted when speech is paused due to potential interruption.
/// </summary>
public record SpeechPausedEvent(
    string SynthesisId,
    string Reason  // "user_speaking", "potential_interruption"
) : AgentEvent;

/// <summary>
/// Emitted when paused speech resumes (false interruption).
/// </summary>
public record SpeechResumedEvent(
    string SynthesisId,
    TimeSpan PauseDuration
) : AgentEvent;

//
// PREEMPTIVE GENERATION EVENTS
//

/// <summary>
/// Emitted when preemptive LLM generation starts before EOT is confirmed.
/// </summary>
public sealed record PreemptiveGenerationStartedEvent(
    PreemptiveGenerationCandidate Candidate
) : AgentEvent
{
    /// <summary>Candidate generation id.</summary>
    public string GenerationId => Candidate.GenerationId;

    /// <summary>Candidate confidence, retained as the old EOT probability projection.</summary>
    public float EndOfTurnProbability => Candidate.Confidence;

    /// <inheritdoc />
    public override EventChannel Channel => EventChannel.Streaming;
}

/// <summary>
/// Emitted when preemptive generation is discarded (user continued speaking).
/// </summary>
public sealed record PreemptiveGenerationDiscardedEvent(
    string GenerationId,
    string Reason
) : AgentEvent
{
    /// <summary>Recognition id that produced the discarded candidate.</summary>
    public string? RecognitionId { get; init; }

    /// <summary>Utterance id that produced the discarded candidate.</summary>
    public string? UtteranceId { get; init; }

    /// <summary>Transcript revision that produced the discarded candidate.</summary>
    public string? TranscriptRevisionId { get; init; }

    /// <inheritdoc />
    public override EventChannel Channel => EventChannel.Control;
}

//
// VAD EVENTS
//

/// <summary>
/// Emitted when voice activity detector detects start of speech.
/// </summary>
public record VadStartOfSpeechEvent(
    TimeSpan AudioTimestamp,
    float SpeechProbability
) : AgentEvent;

/// <summary>
/// Emitted when voice activity detector detects end of speech.
/// </summary>
public record VadEndOfSpeechEvent(
    TimeSpan AudioTimestamp,
    TimeSpan SpeechDuration,
    float SpeechProbability
) : AgentEvent;

//
// AUDIO PIPELINE METRICS (single event for all metrics)
//

/// <summary>
/// Metrics event for audio pipeline observability.
/// </summary>
public record AudioPipelineMetricsEvent(
    string MetricType,      // "latency", "quality", "throughput"
    string MetricName,      // "time_to_first_audio", "synthesis_duration", etc.
    double Value,
    string? Unit = null     // "ms", "bytes", "chunks"
) : AgentEvent;

/// <summary>
/// User-perceived realtime audio experience measurement.
/// </summary>
public sealed record AudioExperienceMetricEvent(
    string MetricName,
    double Value,
    string? Unit = null,
    string? SpeechId = null,
    string? OutputStreamId = null,
    string? SessionId = null,
    string? BranchId = null
) : AgentEvent
{
    /// <inheritdoc />
    public override EventChannel Channel => EventChannel.Streaming;

    /// <inheritdoc />
    public override EventKind Kind => EventKind.Diagnostic;
}

//
// END-OF-TURN EVENTS
//

/// <summary>
/// Emitted when EOT detection determines user has finished speaking.
/// </summary>
public record EotDetectedEvent(
    string TranscribedText,
    float EndOfTurnProbability,
    TimeSpan SilenceDuration,
    string DetectionMethod  // "heuristic-eot", "manual", "timeout"
) : AgentEvent;

//
// FILLER AUDIO EVENTS
//

/// <summary>
/// Emitted when filler audio is played during LLM thinking.
/// </summary>
public record FillerAudioPlayedEvent(
    string Phrase,
    TimeSpan Duration
) : AgentEvent;
