// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json.Serialization;

namespace HPD.Agent.AudioProviders.OpenAI.Stt;

/// <summary>
/// OpenAI-specific STT configuration (Whisper).
/// Only contains settings UNIQUE to OpenAI (not service-agnostic).
/// Service-agnostic settings (Language, ModelId, etc.) are in SttConfig.
/// </summary>
public class OpenAISttConfig
{
    /// <summary>
    /// OpenAI API key.
    /// Can also be set via OPENAI_API_KEY environment variable.
    /// </summary>
    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }

    /// <summary>
    /// Optional base URL override (for Azure OpenAI or proxies).
    /// Default: https://api.openai.com/v1
    /// </summary>
    [JsonPropertyName("baseUrl")]
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Request timeout in seconds.
    /// Default: 30
    /// </summary>
    [JsonPropertyName("timeoutSeconds")]
    public int? TimeoutSeconds { get; set; }

    // Note: Language, ModelId, Temperature, ResponseFormat are in SttConfig (service-agnostic)
}
