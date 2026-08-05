using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.Replicate;

/// <summary>Replicate-specific image-generation operation options.</summary>
public sealed class ReplicateImageOptions : global::HPD.Agent.IImageGenerationProviderOptions
{
    /// <summary>Gets or sets extra model input properties.</summary>
    [JsonPropertyName("input")]
    public Dictionary<string, object?>? Input { get; set; }

    /// <summary>Gets or sets the prediction Prefer header.</summary>
    [JsonPropertyName("prefer")]
    public string? Prefer { get; set; }

    /// <summary>Gets or sets the prediction timeout in seconds.</summary>
    [JsonPropertyName("timeoutSeconds")]
    public int? TimeoutSeconds { get; set; }

    /// <summary>Gets or sets the polling interval in seconds.</summary>
    [JsonPropertyName("pollingIntervalSeconds")]
    public double? PollingIntervalSeconds { get; set; }
}
