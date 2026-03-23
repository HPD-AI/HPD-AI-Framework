// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json.Serialization;

namespace HPD.Agent.AudioProviders.OpenAI.Tts;

/// <summary>
/// JSON serialization context for OpenAI TTS provider-specific configuration.
/// Used for deserializing TtsConfig.ProviderOptionsJson → OpenAITtsConfig.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(OpenAITtsConfig))]
public partial class OpenAITtsJsonContext : JsonSerializerContext
{
}
