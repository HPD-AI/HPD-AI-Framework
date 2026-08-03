// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Audio.ElevenLabs;

/// <summary>ElevenLabs speech-to-text client acquisition configuration.</summary>
public sealed class ElevenLabsSttConfig : global::HPD.Agent.IProviderConfig
{
    /// <summary>Gets or sets the secondary websocket endpoint used for streaming recognition.</summary>
    [JsonPropertyName("webSocketBaseUrl")]
    public string? WebSocketBaseUrl { get; set; }
}
