// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Audio.ElevenLabs;

/// <summary>ElevenLabs text-to-speech client acquisition configuration.</summary>
public sealed class ElevenLabsTtsConfig : global::HPD.Agent.IProviderConfig
{
    /// <summary>Gets or sets the secondary websocket endpoint used for push-text synthesis.</summary>
    [JsonPropertyName("webSocketBaseUrl")]
    public string? WebSocketBaseUrl { get; set; }

    /// <summary>Gets or sets whether the constructed client exposes push-text streaming.</summary>
    [JsonPropertyName("enablePushTextStreaming")]
    public bool EnablePushTextStreaming { get; set; }
}
