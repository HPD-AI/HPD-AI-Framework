// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Audio.OpenAI;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(OpenAISttConfig))]
public partial class OpenAISttJsonContext : JsonSerializerContext
{
}
