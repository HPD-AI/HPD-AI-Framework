// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Audio.ElevenLabs;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ElevenLabsTtsConfig))]
[JsonSerializable(typeof(ElevenLabsSttConfig))]
[JsonSerializable(typeof(ElevenLabsTtsRequest))]
[JsonSerializable(typeof(ElevenLabsSpeechToTextResponse))]
[JsonSerializable(typeof(ElevenLabsWebSocketInitializeMessage))]
[JsonSerializable(typeof(ElevenLabsWebSocketTextMessage))]
[JsonSerializable(typeof(ElevenLabsWebSocketAudioMessage))]
[JsonSerializable(typeof(ElevenLabsWebSocketVoiceSettings))]
[JsonSerializable(typeof(ElevenLabsRealtimeInputAudioChunkMessage))]
internal partial class ElevenLabsTtsJsonContext : JsonSerializerContext
{
}
