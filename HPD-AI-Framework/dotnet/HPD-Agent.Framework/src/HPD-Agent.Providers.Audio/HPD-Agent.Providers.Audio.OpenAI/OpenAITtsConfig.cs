// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Audio.OpenAI;

public sealed class OpenAITtsConfig
{
    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }

    [JsonPropertyName("baseUrl")]
    public string? BaseUrl { get; set; }

    [JsonPropertyName("defaultModelId")]
    public string? DefaultModelId { get; set; }

    [JsonPropertyName("defaultVoiceId")]
    public string? DefaultVoiceId { get; set; }

    [JsonPropertyName("outputFormat")]
    public string? OutputFormat { get; set; }

    [JsonPropertyName("speed")]
    public float? Speed { get; set; }
}
