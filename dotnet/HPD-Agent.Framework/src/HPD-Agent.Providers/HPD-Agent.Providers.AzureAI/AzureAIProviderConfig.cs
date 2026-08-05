using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.AzureAI;

/// <summary>
/// Authentication strategy for Azure AI provider client construction.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AzureAIAuthMode>))]
public enum AzureAIAuthMode
{
    /// <summary>
    /// Use API key authentication when an API key is configured; otherwise use DefaultAzureCredential.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Use API key authentication. Only supported for direct Azure OpenAI-compatible endpoints.
    /// </summary>
    ApiKey = 1,

    /// <summary>
    /// Use DefaultAzureCredential.
    /// </summary>
    DefaultAzureCredential = 2
}

/// <summary>
/// Azure AI Projects service API version used by AIProjectClientOptions.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AzureAIProjectServiceVersion>))]
public enum AzureAIProjectServiceVersion
{
    /// <summary>
    /// Azure AI Projects API version 2025-05-01.
    /// </summary>
    V2025_05_01 = 1,

    /// <summary>
    /// Stable Azure AI Projects API version.
    /// </summary>
    V1 = 2
}

/// <summary>
/// Azure OpenAI service API version used by the downstream AzureOpenAIClientOptions.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AzureAIOpenAIServiceVersion>))]
public enum AzureAIOpenAIServiceVersion
{
    /// <summary>
    /// Azure OpenAI API version 2024-06-01.
    /// </summary>
    V2024_06_01 = 0,

    /// <summary>
    /// Azure OpenAI API version 2024-08-01-preview.
    /// </summary>
    V2024_08_01_Preview = 1,

    /// <summary>
    /// Azure OpenAI API version 2024-09-01-preview.
    /// </summary>
    V2024_09_01_Preview = 2,

    /// <summary>
    /// Azure OpenAI API version 2024-10-01-preview.
    /// </summary>
    V2024_10_01_Preview = 3,

    /// <summary>
    /// Azure OpenAI API version 2024-10-21.
    /// </summary>
    V2024_10_21 = 4,

    /// <summary>
    /// Azure OpenAI API version 2024-12-01-preview.
    /// </summary>
    V2024_12_01_Preview = 5,

    /// <summary>
    /// Azure OpenAI API version 2025-01-01-preview.
    /// </summary>
    V2025_01_01_Preview = 6,

    /// <summary>
    /// Azure OpenAI API version 2025-03-01-preview.
    /// </summary>
    V2025_03_01_Preview = 8,

    /// <summary>
    /// Azure OpenAI API version 2025-04-01-preview.
    /// </summary>
    V2025_04_01_Preview = 9
}

/// <summary>
/// Azure AI Projects-specific provider configuration.
/// </summary>
public class AzureAIProviderConfig : global::HPD.Agent.IProviderConfig
{
    /// <summary>
    /// Authentication strategy used by the provider.
    /// </summary>
    [JsonPropertyName("authMode")]
    public AzureAIAuthMode AuthMode { get; set; } = AzureAIAuthMode.Auto;

    /// <summary>
    /// Azure AI Projects service API version used by AIProjectClientOptions.
    /// </summary>
    [JsonPropertyName("projectServiceVersion")]
    public AzureAIProjectServiceVersion? ProjectServiceVersion { get; set; }

    /// <summary>
    /// Azure OpenAI service API version used for the downstream AzureOpenAIClient.
    /// </summary>
    [JsonPropertyName("openAIServiceVersion")]
    public AzureAIOpenAIServiceVersion? OpenAIServiceVersion { get; set; }

    /// <summary>
    /// Azure OpenAI connection id looked up from the Azure AI project.
    /// </summary>
    [JsonPropertyName("openAIConnectionId")]
    public string? OpenAIConnectionId { get; set; }

    /// <summary>
    /// Optional Entra authentication audience/scope used by the downstream Azure OpenAI client.
    /// </summary>
    [JsonPropertyName("openAIAudience")]
    public string? OpenAIAudience { get; set; }

    /// <summary>
    /// Default request headers applied by the downstream AzureOpenAIClientOptions.
    /// </summary>
    [JsonPropertyName("openAIDefaultHeaders")]
    public Dictionary<string, string>? OpenAIDefaultHeaders { get; set; }

    /// <summary>
    /// Advanced default query parameters applied by the downstream AzureOpenAIClientOptions.
    /// Prefer OpenAIServiceVersion for normal API version selection.
    /// </summary>
    [JsonPropertyName("openAIDefaultQueryParameters")]
    public Dictionary<string, string>? OpenAIDefaultQueryParameters { get; set; }

    /// <summary>
    /// Optional application id appended to Azure SDK user agents.
    /// </summary>
    [JsonPropertyName("userAgentApplicationId")]
    public string? UserAgentApplicationId { get; set; }

    /// <summary>
    /// Network timeout in milliseconds for Azure SDK pipelines.
    /// </summary>
    [JsonPropertyName("networkTimeoutMs")]
    public int? NetworkTimeoutMs { get; set; }

    /// <summary>
    /// Enables or disables distributed tracing in Azure SDK pipelines.
    /// </summary>
    [JsonPropertyName("enableDistributedTracing")]
    public bool? EnableDistributedTracing { get; set; }

}
