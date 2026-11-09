// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Text.Json.Serialization;

namespace HPD.Agent.Stores.HpdBase;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(HpdBaseAgentDefinition))]
[JsonSerializable(typeof(HpdBaseSessionRecord))]
[JsonSerializable(typeof(HpdBaseThreadRecord))]
[JsonSerializable(typeof(HpdBaseThreadEventBlock))]
[JsonSerializable(typeof(HpdBaseThreadEventIdentity))]
[JsonSerializable(typeof(HpdBaseThreadDelta))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class HpdAgentStoreJsonContext : JsonSerializerContext;
