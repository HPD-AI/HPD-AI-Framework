// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.Agent.Audio.Eot;

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

// Interruption Events
[JsonSerializable(typeof(UserInterruptedEvent))]
[JsonSerializable(typeof(SpeechPausedEvent))]
[JsonSerializable(typeof(SpeechResumedEvent))]

// Preemptive Generation Events
[JsonSerializable(typeof(PreemptiveGenerationStartedEvent))]
[JsonSerializable(typeof(PreemptiveGenerationDiscardedEvent))]

// VAD Events
[JsonSerializable(typeof(VadStartOfSpeechEvent))]
[JsonSerializable(typeof(VadEndOfSpeechEvent))]

// Metrics Events
[JsonSerializable(typeof(AudioPipelineMetricsEvent))]

// EOT Events
[JsonSerializable(typeof(EotDetectedEvent))]

// Filler Events
[JsonSerializable(typeof(FillerAudioPlayedEvent))]

// Audio Enums
[JsonSerializable(typeof(AudioProcessingMode))]
[JsonSerializable(typeof(AudioIOMode))]
[JsonSerializable(typeof(EotDetectionStrategy))]
[JsonSerializable(typeof(BackchannelStrategy))]

// Audio Configuration
[JsonSerializable(typeof(AudioConfig))]
[JsonSerializable(typeof(AudioDiagnosticsConfig))]
[JsonSerializable(typeof(EotConfig))]

// Common types
[JsonSerializable(typeof(TimeSpan))]
internal partial class AudioEventJsonContext : JsonSerializerContext { }
