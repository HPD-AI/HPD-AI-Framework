// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Audio.OpenAI;

/// <summary>OpenAI-specific Realtime client acquisition configuration.</summary>
public sealed class OpenAIRealtimeConfig : global::HPD.Agent.IProviderConfig
{
    /// <summary>Gets or sets the OpenAI organization identifier.</summary>
    [JsonPropertyName("organizationId")]
    public string? OrganizationId { get; set; }

    /// <summary>Gets or sets the OpenAI project identifier.</summary>
    [JsonPropertyName("projectId")]
    public string? ProjectId { get; set; }
}
