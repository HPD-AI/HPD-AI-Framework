// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json;
using System.Text.RegularExpressions;

namespace HPD.Agent.Audio.Serialization;

/// <summary>
/// Provides Native AOT compatible JSON serialization for audio events.
/// Uses source-generated serialization for optimal performance.
/// </summary>
/// <remarks>
/// <para>
/// <b>Design Principles:</b>
/// - Events remain pure domain objects (no serialization code)
/// - Version and type fields injected via simple string manipulation
/// - SCREAMING_SNAKE_CASE type discriminators for JSON API convention
/// - Native AOT compatible (zero reflection)
/// </para>
/// <para>
/// <b>Usage:</b>
/// <code>
/// var evt = new AudioChunkEvent("synth-123", base64Audio, "audio/mpeg", 0, duration, false);
/// var json = AudioEventSerializer.ToJson(evt);
/// // {"version":"1.0","type":"AUDIO_CHUNK","synthesisId":"synth-123",...}
/// </code>
/// </para>
/// </remarks>
public static partial class AudioEventSerializer
{
    /// <summary>
    /// Type name to SCREAMING_SNAKE_CASE discriminator mapping for audio events.
    /// </summary>
    private static readonly Dictionary<Type, string> TypeNames = new()
    {
        // Synthesis Events
        [typeof(SynthesisStartedEvent)] = AudioEventTypes.Synthesis.SYNTHESIS_STARTED,
        [typeof(AudioChunkEvent)] = AudioEventTypes.Synthesis.AUDIO_CHUNK,
        [typeof(SynthesisCompletedEvent)] = AudioEventTypes.Synthesis.SYNTHESIS_COMPLETED,

        // Transcription Events
        [typeof(TranscriptionDeltaEvent)] = AudioEventTypes.Transcription.TRANSCRIPTION_DELTA,
        [typeof(TranscriptionCompletedEvent)] = AudioEventTypes.Transcription.TRANSCRIPTION_COMPLETED,

        // Interruption Events
        [typeof(UserInterruptedEvent)] = AudioEventTypes.Interruption.USER_INTERRUPTED,
        [typeof(SpeechPausedEvent)] = AudioEventTypes.Interruption.SPEECH_PAUSED,
        [typeof(SpeechResumedEvent)] = AudioEventTypes.Interruption.SPEECH_RESUMED,

        // Preemptive Generation Events
        [typeof(PreemptiveGenerationStartedEvent)] = AudioEventTypes.PreemptiveGeneration.PREEMPTIVE_GENERATION_STARTED,
        [typeof(PreemptiveGenerationDiscardedEvent)] = AudioEventTypes.PreemptiveGeneration.PREEMPTIVE_GENERATION_DISCARDED,

        // VAD Events
        [typeof(VadStartOfSpeechEvent)] = AudioEventTypes.Vad.VAD_START_OF_SPEECH,
        [typeof(VadEndOfSpeechEvent)] = AudioEventTypes.Vad.VAD_END_OF_SPEECH,

        // Metrics Events
        [typeof(AudioPipelineMetricsEvent)] = AudioEventTypes.Metrics.AUDIO_PIPELINE_METRICS,

        // Turn Detection Events
        [typeof(TurnDetectedEvent)] = AudioEventTypes.TurnDetection.TURN_DETECTED,

        // Filler Events
        [typeof(FillerAudioPlayedEvent)] = AudioEventTypes.Filler.FILLER_AUDIO_PLAYED,
    };

    /// <summary>
    /// Standard JSON options with source generator for Native AOT.
    /// </summary>
    public static JsonSerializerOptions StandardJsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
        TypeInfoResolver = AudioEventJsonContext.Default
    };

    /// <summary>
    /// Serializes an audio event to JSON with version and type fields.
    /// </summary>
    /// <param name="evt">The event to serialize.</param>
    /// <returns>JSON string with standard event format.</returns>
    /// <remarks>
    /// <para>
    /// Output format:
    /// <code>
    /// {
    ///   "version": "1.0",
    ///   "type": "AUDIO_CHUNK",
    ///   "synthesisId": "synth-123",
    ///   "base64Audio": "...",
    ///   ...
    /// }
    /// </code>
    /// </para>
    /// </remarks>
    public static string ToJson(AgentEvent evt)
    {
        return ToJson(evt, "1.0");
    }

    /// <summary>
    /// Serializes an audio event to JSON with specified version.
    /// </summary>
    /// <param name="evt">The event to serialize.</param>
    /// <param name="version">The version string to include.</param>
    /// <returns>JSON string with standard event format.</returns>
    public static string ToJson(AgentEvent evt, string version)
    {
        ArgumentNullException.ThrowIfNull(evt);
        ArgumentNullException.ThrowIfNull(version);

        // Get type discriminator
        var eventType = TypeNames.TryGetValue(evt.GetType(), out var typeName)
            ? typeName
            : ToScreamingSnakeCase(evt.GetType().Name);

        // Serialize event to JSON
        var eventJson = JsonSerializer.Serialize(evt, evt.GetType(), StandardJsonOptions);

        // Inject version and type fields at the beginning
        // JSON always starts with { so we insert after it
        var prefix = $"\"version\":\"{version}\",\"type\":\"{eventType}\"";

        if (eventJson == "{}")
        {
            // Empty object - just add the fields
            return $"{{{prefix}}}";
        }
        else
        {
            // Insert prefix after opening brace
            return eventJson.Insert(1, prefix + ",");
        }
    }

    /// <summary>
    /// Gets the type discriminator for an event type.
    /// </summary>
    /// <param name="eventType">The event type.</param>
    /// <returns>The SCREAMING_SNAKE_CASE type discriminator.</returns>
    public static string GetEventTypeName(Type eventType)
    {
        return TypeNames.TryGetValue(eventType, out var typeName)
            ? typeName
            : ToScreamingSnakeCase(eventType.Name);
    }

    /// <summary>
    /// Gets the type discriminator for an event instance.
    /// </summary>
    /// <param name="evt">The event instance.</param>
    /// <returns>The SCREAMING_SNAKE_CASE type discriminator.</returns>
    public static string GetEventTypeName(AgentEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return GetEventTypeName(evt.GetType());
    }

    /// <summary>
    /// Checks if the given event type is a known audio event.
    /// </summary>
    /// <param name="eventType">The event type to check.</param>
    /// <returns>True if this is a registered audio event type.</returns>
    public static bool IsAudioEvent(Type eventType)
    {
        return TypeNames.ContainsKey(eventType);
    }

    /// <summary>
    /// Checks if the given event is a known audio event.
    /// </summary>
    /// <param name="evt">The event to check.</param>
    /// <returns>True if this is a registered audio event.</returns>
    public static bool IsAudioEvent(AgentEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return IsAudioEvent(evt.GetType());
    }

    /// <summary>
    /// Converts PascalCase event name to SCREAMING_SNAKE_CASE.
    /// Used as fallback for events without explicit mapping.
    /// </summary>
    /// <param name="pascalCase">The PascalCase name (e.g., "AudioChunkEvent").</param>
    /// <returns>The SCREAMING_SNAKE_CASE name (e.g., "AUDIO_CHUNK").</returns>
    private static string ToScreamingSnakeCase(string pascalCase)
    {
        // Remove "Event" suffix if present
        if (pascalCase.EndsWith("Event", StringComparison.Ordinal))
            pascalCase = pascalCase[..^5];

        // Insert underscores before capitals and uppercase
        return PascalCaseToSnakeCaseRegex().Replace(pascalCase, "$1_$2").ToUpperInvariant();
    }

    [GeneratedRegex("([a-z])([A-Z])")]
    private static partial Regex PascalCaseToSnakeCaseRegex();
}
