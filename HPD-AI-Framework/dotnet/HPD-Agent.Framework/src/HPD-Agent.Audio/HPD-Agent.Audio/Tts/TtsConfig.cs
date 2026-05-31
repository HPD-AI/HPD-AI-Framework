// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

namespace HPD.Agent.Audio.Tts;

using System.Text.Json.Serialization;
using HPD.Agent;
using Microsoft.Extensions.AI;

/// <summary>
/// Configuration for Text-to-Speech (TTS) services.
/// Contains service-agnostic settings that work with any TTS provider.
/// </summary>
public class TtsConfig
{
    //
    // SERVICE-AGNOSTIC TTS SETTINGS
    // (Work with ALL TTS providers: OpenAI, ElevenLabs, Google, Azure, etc.)
    //

    /// <summary>
    /// Voice to use for synthesis.
    /// Provider-specific values: OpenAI ("alloy", "echo", "fable", "nova", "onyx", "shimmer"),
    /// ElevenLabs ("rachel", "domi", "bella", etc.), Google ("en-US-Neural2-A", etc.)
    /// </summary>
    public string? Voice { get; set; }

    /// <summary>
    /// Speech speed multiplier. Range: 0.25 - 4.0. Default: 1.0 (normal speed).
    /// Supported by most providers (OpenAI, Google, Azure). ElevenLabs uses stability instead.
    /// </summary>
    public float? Speed { get; set; }

    /// <summary>
    /// Pitch adjustment. Provider-specific interpretation.
    /// Not supported by all providers (e.g., OpenAI ignores this).
    /// </summary>
    public float? Pitch { get; set; }

    /// <summary>
    /// Volume multiplier. Provider-specific interpretation.
    /// Maps directly to <see cref="TextToSpeechOptions.Volume"/>.
    /// </summary>
    public float? Volume { get; set; }

    /// <summary>
    /// Output audio format.
    /// Common values: "mp3", "opus", "aac", "flac", "wav", "pcm"
    /// Supported formats vary by provider.
    /// </summary>
    public string? OutputFormat { get; set; }

    /// <summary>
    /// Output sample rate in Hz.
    /// Common values: 16000, 22050, 24000, 44100, 48000
    /// Higher = better quality but larger files.
    /// </summary>
    public int? SampleRate { get; set; }

    /// <summary>
    /// Model/engine to use for synthesis.
    /// OpenAI: "tts-1" (fast), "tts-1-hd" (quality)
    /// ElevenLabs: "eleven_monolingual_v1", "eleven_turbo_v2"
    /// Google: "standard", "wavenet", "neural2"
    /// </summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// Language code for synthesis (ISO 639-1 or BCP-47).
    /// Examples: "en-US", "es-ES", "fr-FR"
    /// If not set, uses AudioConfig.Language global override.
    /// </summary>
    public string? Language { get; set; }

    //
    // PROVIDER SELECTION
    //

    /// <summary>
    /// TTS provider key (e.g., "openai", "elevenlabs", "google-cloud-tts").
    /// Resolved via the unified provider registry at runtime.
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// Provider-specific configuration as JSON string.
    /// Deserialized by the provider's registered config type.
    ///
    /// Examples:
    /// - OpenAI: {"apiKey":"sk-..."}
    /// - ElevenLabs: {"apiKey":"...", "stability":0.5, "similarityBoost":0.8}
    /// - Google: {"credentialsJson":"..."}
    /// </summary>
    public string? ProviderOptionsJson { get; set; }

    /// <summary>
    /// Additional provider-agnostic request properties.
    /// Values are copied to <see cref="TextToSpeechOptions.AdditionalProperties"/>.
    /// </summary>
    public Dictionary<string, object>? AdditionalProperties { get; set; }

    /// <summary>
    /// Direct runtime TTS client override.
    /// Highest precedence when the audio pipeline resolves text-to-speech.
    /// </summary>
    [JsonIgnore]
    public ITextToSpeechClient? OverrideClient { get; set; }

    /// <summary>
    /// Converts this HPD TTS config into Microsoft.Extensions.AI request options.
    /// </summary>
    public TextToSpeechOptions ToOptions()
    {
        var options = new TextToSpeechOptions
        {
            ModelId = ModelId,
            VoiceId = Voice,
            Language = Language,
            AudioFormat = OutputFormat,
            Speed = Speed,
            Pitch = Pitch,
            Volume = Volume
        };

        if (SampleRate.HasValue)
        {
            (options.AdditionalProperties ??= [])["sampleRate"] = SampleRate.Value;
        }

        if (AdditionalProperties != null)
        {
            foreach (var property in AdditionalProperties)
            {
                (options.AdditionalProperties ??= [])[property.Key] = property.Value;
            }
        }

        return options;
    }

    /// <summary>
    /// Validates TTS configuration.
    /// </summary>
    public void Validate(bool requireProvider = true)
    {
        if (requireProvider && string.IsNullOrWhiteSpace(Provider))
            throw new ArgumentException("TTS Provider is required", nameof(Provider));

        if (Speed is < 0.25f or > 4.0f)
            throw new ArgumentException("Speed must be between 0.25 and 4.0", nameof(Speed));

        if (SampleRate is < 8000 or > 48000)
            throw new ArgumentException("SampleRate must be between 8000 and 48000", nameof(SampleRate));
    }
}
