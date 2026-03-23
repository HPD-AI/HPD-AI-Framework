// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.ElevenLabs;

/// <summary>
/// Configuration for ElevenLabs audio services (TTS and STT).
/// </summary>
public class ElevenLabsAudioConfig
{
    /// <summary>
    /// ElevenLabs API key. Required.
    /// Get yours at: https://elevenlabs.io/app/settings/api-keys
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Base URL for ElevenLabs API.
    /// Default: "https://api.elevenlabs.io/v1"
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.elevenlabs.io/v1";

    /// <summary>
    /// WebSocket URL for streaming TTS.
    /// Default: "wss://api.elevenlabs.io/v1"
    /// </summary>
    public string WebSocketUrl { get; set; } = "wss://api.elevenlabs.io/v1";

    //
    // TTS CONFIGURATION
    //

    /// <summary>
    /// Default voice ID for text-to-speech.
    /// Default: "21m00Tcm4TlvDq8ikWAM" (Rachel - premade voice)
    /// Find voices at: https://elevenlabs.io/app/voice-library
    /// </summary>
    public string DefaultVoiceId { get; set; } = "21m00Tcm4TlvDq8ikWAM";

    /// <summary>
    /// TTS model ID.
    /// Options:
    /// - "eleven_monolingual_v1" - English only, fast
    /// - "eleven_multilingual_v2" - 29 languages
    /// - "eleven_turbo_v2" - Lowest latency
    /// - "eleven_turbo_v2_5" - Latest, balanced (recommended)
    /// Default: "eleven_turbo_v2_5"
    /// </summary>
    public string ModelId { get; set; } = "eleven_turbo_v2_5";

    /// <summary>
    /// Voice stability (0.0 - 1.0).
    /// Higher = more consistent, lower = more expressive.
    /// Default: 0.5
    /// </summary>
    public float Stability { get; set; } = 0.5f;

    /// <summary>
    /// Similarity boost (0.0 - 1.0).
    /// Higher = closer to original voice.
    /// Default: 0.75
    /// </summary>
    public float SimilarityBoost { get; set; } = 0.75f;

    /// <summary>
    /// Style exaggeration (0.0 - 1.0).
    /// Higher = more expressive (experimental).
    /// Default: 0.0
    /// </summary>
    public float Style { get; set; } = 0.0f;

    /// <summary>
    /// Enable speaker boost for better clarity.
    /// Default: true
    /// </summary>
    public bool UseSpeakerBoost { get; set; } = true;

    /// <summary>
    /// Playback speed multiplier (0.5 - 2.0).
    /// 1.0 = normal speed, 1.5 = 50% faster, 0.5 = half speed.
    /// Default: 1.0
    /// </summary>
    public float Speed { get; set; } = 1.0f;

    /// <summary>
    /// Audio output format.
    /// Options: "mp3_44100_128", "mp3_44100_192", "pcm_16000", "pcm_22050", "pcm_24000",
    /// "pcm_44100", "ulaw_8000"
    /// Default: "mp3_44100_128"
    /// </summary>
    public string OutputFormat { get; set; } = "mp3_44100_128";

    /// <summary>
    /// Chunk length schedule for streaming TTS.
    /// Defines character thresholds for chunk generation.
    /// Default: [120, 160, 250, 290] (balanced)
    ///
    /// Examples:
    /// - Ultra-low latency (word-by-word): [50, 80, 120]
    /// - Better quality (sentence): [300, 500, 800]
    /// </summary>
    public int[] ChunkLengthSchedule { get; set; } = [120, 160, 250, 290];

    /// <summary>
    /// Enable word-level timestamps in TTS responses.
    /// Useful for text highlighting sync with audio.
    /// Default: false (reduces latency)
    /// </summary>
    public bool EnableWordTimestamps { get; set; } = false;

    /// <summary>
    /// ISO 639-1 language code for language enforcement (e.g., "en", "es", "fr", "de").
    /// Only supported by eleven_turbo_v2_5 model.
    /// Forces generation in specified language regardless of input text language.
    /// Default: null (auto-detect from text)
    /// </summary>
    public string? LanguageCode { get; set; }

    /// <summary>
    /// Random seed for deterministic audio generation (0 to 4294967295).
    /// Using the same seed with identical text will produce identical audio output.
    /// Useful for testing, caching, and reproducibility.
    /// Default: null (random generation)
    /// </summary>
    public int? Seed { get; set; }

    /// <summary>
    /// Text normalization strategy.
    /// Options: "on", "off", "auto"
    /// - "on": Always normalize text (expand abbreviations, numbers, etc.)
    /// - "off": Never normalize text (use exactly as provided)
    /// - "auto": Automatically decide based on content
    /// Note: Cannot be enabled with eleven_turbo_v2_5 model.
    /// Default: "auto"
    /// </summary>
    public string? ApplyTextNormalization { get; set; } = "auto";

    /// <summary>
    /// Previous text for prosody continuity.
    /// Helps maintain natural speech flow when generating multiple sequential segments.
    /// Useful for long-form content like audiobooks or podcasts.
    /// </summary>
    public string? PreviousText { get; set; }

    /// <summary>
    /// Next text for prosody continuity.
    /// Helps the TTS model anticipate what's coming next for better intonation.
    /// </summary>
    public string? NextText { get; set; }

    /// <summary>
    /// Up to 3 previous request IDs for voice continuity.
    /// Links this request to previous generations for consistent voice characteristics.
    /// Array must contain 3 or fewer request IDs.
    /// </summary>
    public string[]? PreviousRequestIds { get; set; }

    /// <summary>
    /// Up to 3 next request IDs for voice continuity.
    /// Links this request to future generations for consistent voice characteristics.
    /// Array must contain 3 or fewer request IDs.
    /// </summary>
    public string[]? NextRequestIds { get; set; }

    /// <summary>
    /// Pronunciation dictionary ID for custom word pronunciations.
    /// Create custom dictionaries at: https://elevenlabs.io/app/pronunciation-dictionaries
    /// Useful for proper nouns, technical terms, or brand names.
    /// </summary>
    public string? PronunciationDictionaryId { get; set; }

    /// <summary>
    /// Version ID of the pronunciation dictionary to use.
    /// Allows using specific versions of a pronunciation dictionary.
    /// </summary>
    public string? PronunciationDictionaryVersionId { get; set; }

    //
    // STT CONFIGURATION
    //

    /// <summary>
    /// STT model ID.
    /// Default: "scribe_v1"
    /// </summary>
    public string SttModelId { get; set; } = "scribe_v1";

    /// <summary>
    /// STT response format.
    /// Options: "json", "text", "srt", "vtt"
    /// Default: "json"
    /// </summary>
    public string SttResponseFormat { get; set; } = "json";

    //
    // TIMEOUT CONFIGURATION
    //

    /// <summary>
    /// HTTP client timeout for non-streaming requests.
    /// Default: 30 seconds
    /// </summary>
    public TimeSpan HttpTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// WebSocket connection timeout.
    /// Default: 10 seconds
    /// </summary>
    public TimeSpan WebSocketConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// WebSocket message receive timeout.
    /// Default: 30 seconds
    /// </summary>
    public TimeSpan WebSocketReceiveTimeout { get; set; } = TimeSpan.FromSeconds(30);

    //
    // VALIDATION
    //

    /// <summary>
    /// Validates the configuration.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if API key is missing.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException(
                "ElevenLabs API key is required. Get yours at: https://elevenlabs.io/app/settings/api-keys");
        }

        if (Stability < 0.0f || Stability > 1.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(Stability), "Must be between 0.0 and 1.0");
        }

        if (SimilarityBoost < 0.0f || SimilarityBoost > 1.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(SimilarityBoost), "Must be between 0.0 and 1.0");
        }

        if (Style < 0.0f || Style > 1.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(Style), "Must be between 0.0 and 1.0");
        }

        if (Speed < 0.5f || Speed > 2.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(Speed), "Must be between 0.5 and 2.0");
        }

        if (Seed.HasValue && (Seed.Value < 0 || Seed.Value > 4294967295))
        {
            throw new ArgumentOutOfRangeException(nameof(Seed), "Must be between 0 and 4294967295");
        }

        if (PreviousRequestIds != null && PreviousRequestIds.Length > 3)
        {
            throw new ArgumentException("Cannot have more than 3 previous request IDs", nameof(PreviousRequestIds));
        }

        if (NextRequestIds != null && NextRequestIds.Length > 3)
        {
            throw new ArgumentException("Cannot have more than 3 next request IDs", nameof(NextRequestIds));
        }

        if (ApplyTextNormalization != null &&
            ApplyTextNormalization != "on" &&
            ApplyTextNormalization != "off" &&
            ApplyTextNormalization != "auto")
        {
            throw new ArgumentException("Must be 'on', 'off', or 'auto'", nameof(ApplyTextNormalization));
        }
    }
}
