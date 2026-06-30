// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Audio.ElevenLabs;

internal sealed class ElevenLabsTtsRequest
{
    [JsonPropertyName("text")]
    public required string Text { get; init; }

    [JsonPropertyName("model_id")]
    public string? ModelId { get; init; }

    [JsonPropertyName("language_code")]
    public string? LanguageCode { get; init; }

    [JsonPropertyName("voice_settings")]
    public ElevenLabsVoiceSettings? VoiceSettings { get; init; }

    [JsonPropertyName("apply_text_normalization")]
    public string? ApplyTextNormalization { get; init; }
}

internal sealed class ElevenLabsVoiceSettings
{
    [JsonPropertyName("stability")]
    public double? Stability { get; init; }

    [JsonPropertyName("similarity_boost")]
    public double? SimilarityBoost { get; init; }

    [JsonPropertyName("style")]
    public double? Style { get; init; }

    [JsonPropertyName("use_speaker_boost")]
    public bool? UseSpeakerBoost { get; init; }

    [JsonPropertyName("speed")]
    public double? Speed { get; init; }
}
