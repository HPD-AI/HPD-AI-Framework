// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Audio.ElevenLabs;

public sealed class ElevenLabsSttConfig
{
    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }

    [JsonPropertyName("baseUrl")]
    public string? BaseUrl { get; set; }

    [JsonPropertyName("webSocketBaseUrl")]
    public string? WebSocketBaseUrl { get; set; }

    [JsonPropertyName("defaultModelId")]
    public string? DefaultModelId { get; set; }

    [JsonPropertyName("realtimeModelId")]
    public string? RealtimeModelId { get; set; }

    [JsonPropertyName("languageCode")]
    public string? LanguageCode { get; set; }

    [JsonPropertyName("diarize")]
    public bool? Diarize { get; set; }

    [JsonPropertyName("tagAudioEvents")]
    public bool? TagAudioEvents { get; set; }

    [JsonPropertyName("timestampsGranularity")]
    public string? TimestampsGranularity { get; set; }

    [JsonPropertyName("audioFormat")]
    public string? AudioFormat { get; set; }

    [JsonPropertyName("commitStrategy")]
    public string? CommitStrategy { get; set; }

    [JsonPropertyName("includeTimestamps")]
    public bool? IncludeTimestamps { get; set; }

    [JsonPropertyName("includeLanguageDetection")]
    public bool? IncludeLanguageDetection { get; set; }

    [JsonPropertyName("keyterms")]
    public string[]? Keyterms { get; set; }

    [JsonPropertyName("noVerbatim")]
    public bool? NoVerbatim { get; set; }

    [JsonPropertyName("vadSilenceThresholdSeconds")]
    public double? VadSilenceThresholdSeconds { get; set; }

    [JsonPropertyName("vadThreshold")]
    public double? VadThreshold { get; set; }

    [JsonPropertyName("minSpeechDurationMilliseconds")]
    public int? MinSpeechDurationMilliseconds { get; set; }

    [JsonPropertyName("minSilenceDurationMilliseconds")]
    public int? MinSilenceDurationMilliseconds { get; set; }

    [JsonPropertyName("enableLogging")]
    public bool? EnableLogging { get; set; }

    [JsonPropertyName("streamingChunkSizeBytes")]
    public int? StreamingChunkSizeBytes { get; set; }
}
