// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json.Serialization;

namespace HPD.Agent.AudioProviders.ElevenLabs.Tts;

/// <summary>
/// ElevenLabs-specific TTS configuration.
/// Contains ElevenLabs-unique settings (stability, similarity boost, etc.).
/// Service-agnostic settings (Voice, Speed) are in TtsConfig.
/// </summary>
public class ElevenLabsTtsConfig
{
    /// <summary>
    /// ElevenLabs API key.
    /// Get yours at: https://elevenlabs.io/app/settings/api-keys
    /// </summary>
    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }

    /// <summary>
    /// Base URL for ElevenLabs API.
    /// Default: "https://api.elevenlabs.io/v1"
    /// </summary>
    [JsonPropertyName("baseUrl")]
    public string? BaseUrl { get; set; }

    /// <summary>
    /// WebSocket URL for streaming TTS.
    /// Default: "wss://api.elevenlabs.io/v1"
    /// </summary>
    [JsonPropertyName("webSocketUrl")]
    public string? WebSocketUrl { get; set; }

    /// <summary>
    /// Voice stability (0.0 - 1.0).
    /// Higher = more consistent, lower = more expressive.
    /// Default: 0.5
    /// </summary>
    [JsonPropertyName("stability")]
    public float? Stability { get; set; }

    /// <summary>
    /// Similarity boost (0.0 - 1.0).
    /// Higher = closer to original voice.
    /// Default: 0.75
    /// </summary>
    [JsonPropertyName("similarityBoost")]
    public float? SimilarityBoost { get; set; }

    /// <summary>
    /// Style exaggeration (0.0 - 1.0).
    /// Higher = more expressive (experimental).
    /// Default: 0.0
    /// </summary>
    [JsonPropertyName("style")]
    public float? Style { get; set; }

    /// <summary>
    /// Enable speaker boost for better clarity.
    /// Default: true
    /// </summary>
    [JsonPropertyName("useSpeakerBoost")]
    public bool? UseSpeakerBoost { get; set; }

    /// <summary>
    /// Chunk length schedule for streaming TTS.
    /// Defines character thresholds for chunk generation.
    /// Default: [120, 160, 250, 290] (balanced)
    /// </summary>
    [JsonPropertyName("chunkLengthSchedule")]
    public int[]? ChunkLengthSchedule { get; set; }

    /// <summary>
    /// Enable word-level timestamps in TTS responses.
    /// Default: false (reduces latency)
    /// </summary>
    [JsonPropertyName("enableWordTimestamps")]
    public bool? EnableWordTimestamps { get; set; }

    /// <summary>
    /// Random seed for deterministic audio generation (0 to 4294967295).
    /// Default: null (random generation)
    /// </summary>
    [JsonPropertyName("seed")]
    public int? Seed { get; set; }

    /// <summary>
    /// Text normalization strategy: "on", "off", "auto"
    /// Cannot be enabled with eleven_turbo_v2_5 model.
    /// Default: "auto"
    /// </summary>
    [JsonPropertyName("applyTextNormalization")]
    public string? ApplyTextNormalization { get; set; }

    /// <summary>
    /// Previous text for prosody continuity.
    /// </summary>
    [JsonPropertyName("previousText")]
    public string? PreviousText { get; set; }

    /// <summary>
    /// Next text for prosody continuity.
    /// </summary>
    [JsonPropertyName("nextText")]
    public string? NextText { get; set; }

    /// <summary>
    /// Up to 3 previous request IDs for voice continuity.
    /// </summary>
    [JsonPropertyName("previousRequestIds")]
    public string[]? PreviousRequestIds { get; set; }

    /// <summary>
    /// Up to 3 next request IDs for voice continuity.
    /// </summary>
    [JsonPropertyName("nextRequestIds")]
    public string[]? NextRequestIds { get; set; }

    /// <summary>
    /// Pronunciation dictionary ID for custom word pronunciations.
    /// </summary>
    [JsonPropertyName("pronunciationDictionaryId")]
    public string? PronunciationDictionaryId { get; set; }

    /// <summary>
    /// Version ID of the pronunciation dictionary to use.
    /// </summary>
    [JsonPropertyName("pronunciationDictionaryVersionId")]
    public string? PronunciationDictionaryVersionId { get; set; }

    /// <summary>
    /// HTTP client timeout for non-streaming requests (seconds).
    /// Default: 30
    /// </summary>
    [JsonPropertyName("httpTimeoutSeconds")]
    public int? HttpTimeoutSeconds { get; set; }

    /// <summary>
    /// WebSocket connection timeout (seconds).
    /// Default: 10
    /// </summary>
    [JsonPropertyName("webSocketConnectTimeoutSeconds")]
    public int? WebSocketConnectTimeoutSeconds { get; set; }

    /// <summary>
    /// WebSocket message receive timeout (seconds).
    /// Default: 30
    /// </summary>
    [JsonPropertyName("webSocketReceiveTimeoutSeconds")]
    public int? WebSocketReceiveTimeoutSeconds { get; set; }

    // Note: Voice is in TtsConfig.Voice (voice ID like "21m00Tcm4TlvDq8ikWAM")
    // Note: Model is in TtsConfig.ModelId (e.g., "eleven_turbo_v2_5")
    // Note: Speed is in TtsConfig.Speed (playback speed multiplier)
    // Note: Language is in TtsConfig.Language (ISO 639-1 code)
    // Note: OutputFormat is in TtsConfig.OutputFormat (format string)
}
