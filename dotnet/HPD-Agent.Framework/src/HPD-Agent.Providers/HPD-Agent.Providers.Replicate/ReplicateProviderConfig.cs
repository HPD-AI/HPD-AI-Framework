using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Replicate;

/// <summary>
/// Replicate-specific image generation configuration.
/// </summary>
public class ReplicateProviderConfig : global::HPD.Agent.IImageGenerationProviderOptions
{
    /// <summary>
    /// Replicate model owner. If omitted, the provider parses ModelName as owner/model.
    /// </summary>
    [JsonPropertyName("modelOwner")]
    public string? ModelOwner { get; set; }

    /// <summary>
    /// Extra input properties sent to the Replicate model along with the prompt.
    /// </summary>
    [JsonPropertyName("input")]
    public Dictionary<string, object?>? Input { get; set; }

    /// <summary>
    /// Prefer header value for Replicate prediction creation. Defaults to wait=60.
    /// </summary>
    [JsonPropertyName("prefer")]
    public string? Prefer { get; set; }

    /// <summary>
    /// Maximum time to poll when Replicate returns an incomplete prediction.
    /// </summary>
    [JsonPropertyName("timeoutSeconds")]
    public int? TimeoutSeconds { get; set; }

    /// <summary>
    /// Polling interval when waiting for an incomplete prediction.
    /// </summary>
    [JsonPropertyName("pollingIntervalSeconds")]
    public double? PollingIntervalSeconds { get; set; }

    /// <summary>
    /// Media type assigned to URL outputs. Defaults to image/webp.
    /// </summary>
    [JsonPropertyName("outputMediaType")]
    public string? OutputMediaType { get; set; }
}
