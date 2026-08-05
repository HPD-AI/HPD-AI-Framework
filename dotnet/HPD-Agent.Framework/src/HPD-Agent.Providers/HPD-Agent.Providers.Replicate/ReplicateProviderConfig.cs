using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Replicate;

/// <summary>
/// Replicate-specific image generation configuration.
/// </summary>
public sealed class ReplicateProviderConfig : global::HPD.Agent.IProviderConfig
{
    /// <summary>
    /// Replicate model owner. If omitted, the provider parses ModelName as owner/model.
    /// </summary>
    [JsonPropertyName("modelOwner")]
    public string? ModelOwner { get; set; }

}
