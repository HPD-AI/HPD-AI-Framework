// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Text.Json.Serialization;
using HPD.Agent.Audio;
using HPD.Agent.Audio.Output;

namespace HPD.Agent.Providers.Audio.ElevenLabs;

public sealed class ElevenLabsTtsConfig : global::HPD.Agent.ITextToSpeechProviderOptions
{
    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }

    [JsonPropertyName("baseUrl")]
    public string? BaseUrl { get; set; }

    [JsonPropertyName("webSocketBaseUrl")]
    public string? WebSocketBaseUrl { get; set; }

    [JsonPropertyName("defaultModelId")]
    public string? DefaultModelId { get; set; }

    [JsonPropertyName("defaultVoiceId")]
    public string? DefaultVoiceId { get; set; }

    [JsonPropertyName("outputFormat")]
    public string? OutputFormat { get; set; }

    [JsonPropertyName("stability")]
    public double? Stability { get; set; }

    [JsonPropertyName("similarityBoost")]
    public double? SimilarityBoost { get; set; }

    [JsonPropertyName("style")]
    public double? Style { get; set; }

    [JsonPropertyName("useSpeakerBoost")]
    public bool? UseSpeakerBoost { get; set; }

    [JsonPropertyName("speed")]
    public double? Speed { get; set; }

    [JsonPropertyName("applyTextNormalization")]
    public string? ApplyTextNormalization { get; set; }

    [JsonPropertyName("enablePushTextStreaming")]
    public bool EnablePushTextStreaming { get; set; }

    [JsonPropertyName("pushTextAggregationMode")]
    public PushTextInputAggregationMode PushTextAggregationMode { get; set; } =
        PushTextInputAggregationMode.Sentence;

    [JsonPropertyName("autoMode")]
    public bool? AutoMode { get; set; }

    [JsonPropertyName("syncAlignment")]
    public bool? SyncAlignment { get; set; }

    [JsonPropertyName("inactivityTimeout")]
    public int? InactivityTimeout { get; set; }
}
