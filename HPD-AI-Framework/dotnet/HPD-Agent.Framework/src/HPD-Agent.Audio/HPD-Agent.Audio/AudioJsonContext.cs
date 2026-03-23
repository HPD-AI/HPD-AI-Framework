// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json.Serialization;
using HPD.Agent.Audio.Stt;
using HPD.Agent.Audio.Tts;
using HPD.Agent.Audio.Vad;

namespace HPD.Agent.Audio;

/// <summary>
/// JSON serialization context for core audio configuration types.
/// Used for serializing AudioConfig and its nested role configs (TtsConfig, SttConfig, VadConfig).
/// This context does NOT know about provider-specific configs (OpenAITtsConfig, etc.).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AudioConfig))]
[JsonSerializable(typeof(AudioRunConfig))]
[JsonSerializable(typeof(TtsConfig))]
[JsonSerializable(typeof(SttConfig))]
[JsonSerializable(typeof(VadConfig))]
[JsonSerializable(typeof(AudioProcessingMode))]
[JsonSerializable(typeof(AudioIOMode))]
[JsonSerializable(typeof(TurnDetectionStrategy))]
[JsonSerializable(typeof(BackchannelStrategy))]
[JsonSerializable(typeof(FillerStrategy))]
[JsonSerializable(typeof(ValidationResult))]
[JsonSerializable(typeof(HashSet<string>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(Dictionary<string, object>))]
public partial class AudioJsonContext : JsonSerializerContext
{
}
