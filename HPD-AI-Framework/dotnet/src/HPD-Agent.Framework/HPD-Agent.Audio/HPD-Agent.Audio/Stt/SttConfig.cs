// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Stt;

/// <summary>
/// Configuration for Speech-to-Text (STT) services.
/// Contains service-agnostic settings that work with any STT provider.
/// </summary>
public class SttConfig
{
    //
    // SERVICE-AGNOSTIC STT SETTINGS
    // (Work with ALL STT providers: OpenAI Whisper, Deepgram, Google, Azure, AssemblyAI, etc.)
    //

    /// <summary>
    /// Language code for transcription (ISO 639-1 or BCP-47).
    /// Examples: "en-US", "es-ES", "fr-FR", "auto" (auto-detect)
    /// If not set, uses AudioConfig.Language global override.
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// Model/engine to use for transcription.
    /// OpenAI: "whisper-1"
    /// Deepgram: "nova-2", "nova", "enhanced", "base"
    /// Google: "latest_long", "latest_short", "command_and_search"
    /// </summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// Temperature for transcription (controls randomness).
    /// Range: 0.0 (deterministic) - 1.0 (creative).
    /// Not supported by all providers.
    /// </summary>
    public float? Temperature { get; set; }

    /// <summary>
    /// Response format for transcription results.
    /// Common values: "text", "json", "verbose_json", "vtt", "srt"
    /// OpenAI Whisper: supports all
    /// Deepgram: "json" only
    /// </summary>
    public string? ResponseFormat { get; set; }

    /// <summary>
    /// Additional provider-agnostic properties for STT.
    /// May include: punctuation, profanity_filter, word_timestamps, etc.
    /// </summary>
    public Dictionary<string, object>? AdditionalProperties { get; set; }

    //
    // PROVIDER SELECTION
    //

    /// <summary>
    /// STT provider key (e.g., "openai-audio", "deepgram", "google-cloud-stt").
    /// Resolved via SttProviderDiscovery at runtime.
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Provider-specific configuration as JSON string.
    /// Deserialized by the provider's registered config type.
    ///
    /// Examples:
    /// - OpenAI: {"apiKey":"sk-..."}
    /// - Deepgram: {"apiKey":"...", "tier":"enhanced", "keywords":["HPD","agent"]}
    /// - Google: {"credentialsJson":"..."}
    /// </summary>
    public string? ProviderOptionsJson { get; set; }

    /// <summary>
    /// Validates STT configuration.
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Provider))
            throw new ArgumentException("STT Provider is required", nameof(Provider));

        if (Temperature is < 0.0f or > 1.0f)
            throw new ArgumentException("Temperature must be between 0.0 and 1.0", nameof(Temperature));
    }
}
