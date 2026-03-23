// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json.Serialization;

namespace HPD.Agent.AudioProviders.ElevenLabs.Tts;

/// <summary>
/// JSON serialization context for ElevenLabs TTS provider-specific configuration.
/// Used for deserializing TtsConfig.ProviderOptionsJson → ElevenLabsTtsConfig.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ElevenLabsTtsConfig))]
public partial class ElevenLabsTtsJsonContext : JsonSerializerContext
{
}
