// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Audio.OpenAI;

public sealed class OpenAIRealtimeConfig
{
    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }

    [JsonPropertyName("baseUrl")]
    public string? BaseUrl { get; set; }

    [JsonPropertyName("defaultModelId")]
    public string? DefaultModelId { get; set; }

    [JsonPropertyName("organizationId")]
    public string? OrganizationId { get; set; }

    [JsonPropertyName("projectId")]
    public string? ProjectId { get; set; }
}
