// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Audio.OpenAI;

[JsonSerializable(typeof(OpenAIRealtimeConfig))]
public partial class OpenAIRealtimeJsonContext : JsonSerializerContext;
