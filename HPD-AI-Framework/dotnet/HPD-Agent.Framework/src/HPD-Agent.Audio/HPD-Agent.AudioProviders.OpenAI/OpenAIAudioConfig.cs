// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json.Serialization;

namespace HPD.Agent.AudioProviders.OpenAI;

/// <summary>
/// OpenAI-specific audio configuration for FFI/JSON serialization.
/// Used when configuring OpenAI audio via AgentConfig.Audio.TtsProviderOptionsJson.
/// </summary>
public class OpenAIAudioConfig
{
    /// <summary>
    /// OpenAI API key. If not provided, falls back to OPENAI_API_KEY environment variable.
    /// </summary>
    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }

    /// <summary>
    /// TTS model to use. Default: "tts-1".
    /// Options: "tts-1", "tts-1-hd"
    /// </summary>
    [JsonPropertyName("model")]
    public string? Model { get; set; }

    /// <summary>
    /// Voice to use for TTS. Default: "alloy".
    /// Options: "alloy", "echo", "fable", "onyx", "nova", "shimmer"
    /// </summary>
    [JsonPropertyName("voice")]
    public string? Voice { get; set; }

    /// <summary>
    /// Base URL for OpenAI API. Default: "https://api.openai.com/v1"
    /// Can be overridden for custom endpoints or proxies.
    /// </summary>
    [JsonPropertyName("baseUrl")]
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Request timeout in seconds. Default: 30
    /// </summary>
    [JsonPropertyName("timeoutSeconds")]
    public int? TimeoutSeconds { get; set; }
}
