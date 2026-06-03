// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Audio.OpenAI;

public sealed class OpenAISttConfig
{
    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }

    [JsonPropertyName("baseUrl")]
    public string? BaseUrl { get; set; }

    [JsonPropertyName("defaultModelId")]
    public string? DefaultModelId { get; set; }

    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; }

    [JsonPropertyName("temperature")]
    public float? Temperature { get; set; }

    [JsonPropertyName("responseFormat")]
    public string? ResponseFormat { get; set; }

    [JsonPropertyName("timestampGranularities")]
    public string[]? TimestampGranularities { get; set; }

    [JsonPropertyName("includeLogprobs")]
    public bool? IncludeLogprobs { get; set; }
}
