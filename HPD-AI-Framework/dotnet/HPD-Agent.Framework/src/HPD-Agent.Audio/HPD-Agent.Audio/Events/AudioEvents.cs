// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

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
/// Emitted when preemptive LLM generation starts before turn is confirmed.
/// </summary>
public record PreemptiveGenerationStartedEvent(
    string GenerationId,
    float TurnCompletionProbability
) : AgentEvent;

/// <summary>
/// Emitted when preemptive generation is discarded (user continued speaking).
/// </summary>
public record PreemptiveGenerationDiscardedEvent(
    string GenerationId,
    string Reason  // "user_continued", "low_confidence"
) : AgentEvent;

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

//
// TURN DETECTION EVENTS
//

/// <summary>
/// Emitted when turn detection determines user has finished speaking.
/// </summary>
public record TurnDetectedEvent(
    string TranscribedText,
    float CompletionProbability,
    TimeSpan SilenceDuration,
    string DetectionMethod  // "heuristic", "ml", "manual", "timeout"
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
