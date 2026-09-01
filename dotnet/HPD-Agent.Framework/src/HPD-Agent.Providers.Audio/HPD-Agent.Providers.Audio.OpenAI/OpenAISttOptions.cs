// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Audio.OpenAI;

/// <summary>OpenAI-specific speech-to-text operation options.</summary>
public sealed class OpenAISttOptions : global::HPD.Agent.ISpeechToTextProviderOptions
{
    /// <summary>Gets or sets the transcription prompt.</summary>
    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; }

    /// <summary>Gets or sets transcription sampling temperature.</summary>
    [JsonPropertyName("temperature")]
    public float? Temperature { get; set; }

    /// <summary>Gets or sets the OpenAI transcription response format.</summary>
    [JsonPropertyName("responseFormat")]
    public string? ResponseFormat { get; set; }

    /// <summary>Gets or sets requested timestamp granularities.</summary>
    [JsonPropertyName("timestampGranularities")]
    public string[]? TimestampGranularities { get; set; }

    /// <summary>Gets or sets whether token log probabilities are requested.</summary>
    [JsonPropertyName("includeLogprobs")]
    public bool? IncludeLogprobs { get; set; }

    /// <summary>Gets or sets literal vocabulary hints for retained realtime transcription.</summary>
    [JsonPropertyName("keywords")]
    public string[]? Keywords { get; set; }

    /// <summary>Gets or sets the retained transcription delay: minimal, low, medium, high, or xhigh.</summary>
    [JsonPropertyName("realtimeDelay")]
    public string? RealtimeDelay { get; set; }
}
