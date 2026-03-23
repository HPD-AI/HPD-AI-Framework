// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json.Serialization;

namespace HPD.Agent.AudioProviders.OpenAI.Stt;

/// <summary>
/// JSON serialization context for OpenAI STT provider-specific configuration.
/// Used for deserializing SttConfig.ProviderOptionsJson → OpenAISttConfig.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(OpenAISttConfig))]
public partial class OpenAISttJsonContext : JsonSerializerContext
{
}
