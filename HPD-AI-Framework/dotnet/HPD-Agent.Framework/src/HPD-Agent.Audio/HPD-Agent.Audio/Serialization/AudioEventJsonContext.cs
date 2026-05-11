// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent.Audio.Eot;
using HPD.Agent.Audio.Output;
using HPD.Agent.Audio.Preemptive;
using HPD.Agent.Audio.Recognition;
using HPD.Agent.Audio.Turn;

namespace HPD.Agent.Audio.Serialization;

/// <summary>
/// Source generator context for Native AOT compatible audio event serialization.
/// All audio event types must be registered here for proper serialization.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false
)]
// Synthesis Events
[JsonSerializable(typeof(SynthesisStartedEvent))]
[JsonSerializable(typeof(AudioChunkEvent))]
[JsonSerializable(typeof(SynthesisCompletedEvent))]

// Transcription Events
[JsonSerializable(typeof(TranscriptionDeltaEvent))]
[JsonSerializable(typeof(TranscriptionCompletedEvent))]

// Speech Recognition Events
[JsonSerializable(typeof(SpeechRecognitionContext))]
[JsonSerializable(typeof(SpeechRecognitionTranscript))]
[JsonSerializable(typeof(SpeechRecognitionWord))]
[JsonSerializable(typeof(SpeechRecognitionStartedEvent))]
[JsonSerializable(typeof(SpeechRecognitionInterimEvent))]
[JsonSerializable(typeof(SpeechRecognitionPreflightEvent))]
[JsonSerializable(typeof(SpeechRecognitionFinalEvent))]
[JsonSerializable(typeof(SpeechRecognitionUsageEvent))]
[JsonSerializable(typeof(SpeechRecognitionEndedEvent))]
[JsonSerializable(typeof(SpeechRecognitionErrorEvent))]

// Speech Output Events
[JsonSerializable(typeof(SpeechOutputContext))]
[JsonSerializable(typeof(SpeechOutputState))]
[JsonSerializable(typeof(AudioChunkFrame))]
[JsonSerializable(typeof(SpeechOutputStartedEvent))]
[JsonSerializable(typeof(SpeechOutputTextQueuedEvent))]
[JsonSerializable(typeof(SpeechOutputAudioQueuedEvent))]
[JsonSerializable(typeof(SpeechOutputPlaybackStartedEvent))]
[JsonSerializable(typeof(SpeechOutputPlaybackProgressEvent))]
[JsonSerializable(typeof(SpeechOutputPlaybackFinishedEvent))]
[JsonSerializable(typeof(SpeechOutputPausedEvent))]
[JsonSerializable(typeof(SpeechOutputResumedEvent))]
[JsonSerializable(typeof(SpeechOutputInterruptedEvent))]
[JsonSerializable(typeof(SpeechOutputCompletedEvent))]
[JsonSerializable(typeof(SpeechOutputErrorEvent))]

// User Turn Events
[JsonSerializable(typeof(UserTurnContext))]
[JsonSerializable(typeof(EndpointingDecision))]
[JsonSerializable(typeof(UserTurnStartedEvent))]
[JsonSerializable(typeof(UserTurnUpdatedEvent))]
[JsonSerializable(typeof(UserTurnReadyEvent))]
[JsonSerializable(typeof(UserTurnCommittedEvent))]
[JsonSerializable(typeof(UserTurnCancelledEvent))]

// Interruption Events
[JsonSerializable(typeof(UserInterruptedEvent))]
[JsonSerializable(typeof(SpeechPausedEvent))]
[JsonSerializable(typeof(SpeechResumedEvent))]

// Preemptive Generation Events
[JsonSerializable(typeof(PreemptiveGenerationCandidate))]
[JsonSerializable(typeof(PreemptiveGenerationStartedEvent))]
[JsonSerializable(typeof(PreemptiveGenerationDiscardedEvent))]

// VAD Events
[JsonSerializable(typeof(VadStartOfSpeechEvent))]
[JsonSerializable(typeof(VadEndOfSpeechEvent))]

// Metrics Events
[JsonSerializable(typeof(AudioPipelineMetricsEvent))]
[JsonSerializable(typeof(AudioExperienceMetricEvent))]

// EOT Events
[JsonSerializable(typeof(EotDetectedEvent))]

// Filler Events
[JsonSerializable(typeof(FillerAudioPlayedEvent))]

// Audio Enums
[JsonSerializable(typeof(AudioProcessingMode))]
[JsonSerializable(typeof(AudioIOMode))]
[JsonSerializable(typeof(EotDetectionStrategy))]
[JsonSerializable(typeof(EndpointingMode))]
[JsonSerializable(typeof(TurnControllerState))]
[JsonSerializable(typeof(BackchannelStrategy))]

// Audio Configuration
[JsonSerializable(typeof(AudioConfig))]
[JsonSerializable(typeof(AudioDiagnosticsConfig))]
[JsonSerializable(typeof(EotConfig))]
[JsonSerializable(typeof(SpeechRecognitionCapabilities))]
[JsonSerializable(typeof(SpeechRecognitionOptions))]

// Common types
[JsonSerializable(typeof(TimeSpan))]
internal partial class AudioEventJsonContext : JsonSerializerContext { }
