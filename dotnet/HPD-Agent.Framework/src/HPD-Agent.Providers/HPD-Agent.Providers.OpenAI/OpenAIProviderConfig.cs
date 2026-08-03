using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HPD.Agent.Providers.OpenAI;

/// <summary>
/// Selects which OpenAI chat API backs the provider-created chat client.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<OpenAIChatApi>))]
public enum OpenAIChatApi
{
    /// <summary>
    /// Use OpenAI's Responses API via Microsoft.Extensions.AI.
    /// </summary>
    Responses = 0,

    /// <summary>
    /// Use OpenAI's chat completions API via Microsoft.Extensions.AI.
    /// </summary>
    ChatCompletions = 1
}

/// <summary>
/// Azure OpenAI service API version used by AzureOpenAIClientOptions.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<AzureOpenAIServiceVersion>))]
public enum AzureOpenAIServiceVersion
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
/// OpenAI-specific provider configuration options.
/// </summary>
public class OpenAIProviderConfig : global::HPD.Agent.IProviderConfig
{
    /// <summary>
    /// Selects the OpenAI chat API used to construct chat clients.
    /// Runtime model-call behavior belongs in ChatClientConfig.
    /// </summary>
    [JsonPropertyName("chatApi")]
    public OpenAIChatApi ChatApi { get; set; } = OpenAIChatApi.Responses;

    /// <summary>
    /// Optional OpenAI organization id applied to the SDK client.
    /// </summary>
    [JsonPropertyName("organizationId")]
    public string? OrganizationId { get; set; }

    /// <summary>
    /// Optional OpenAI project id applied to the SDK client.
    /// </summary>
    [JsonPropertyName("projectId")]
    public string? ProjectId { get; set; }

    /// <summary>
    /// Optional application id appended to the SDK user agent.
    /// </summary>
    [JsonPropertyName("userAgentApplicationId")]
    public string? UserAgentApplicationId { get; set; }

    /// <summary>
    /// Network timeout in milliseconds for the SDK pipeline.
    /// </summary>
    [JsonPropertyName("networkTimeoutMs")]
    public int? NetworkTimeoutMs { get; set; }

    /// <summary>
    /// Enables or disables distributed tracing in the SDK pipeline.
    /// </summary>
    [JsonPropertyName("enableDistributedTracing")]
    public bool? EnableDistributedTracing { get; set; }
}

/// <summary>
/// Azure OpenAI-specific provider configuration options.
/// </summary>
public class AzureOpenAIProviderConfig : global::HPD.Agent.IProviderConfig
{
    /// <summary>
    /// Selects the Azure OpenAI chat API used to construct chat clients.
    /// Runtime model-call behavior belongs in ChatClientConfig.
    /// </summary>
    [JsonPropertyName("chatApi")]
    public OpenAIChatApi ChatApi { get; set; } = OpenAIChatApi.Responses;

    /// <summary>
    /// Azure OpenAI service API version used by AzureOpenAIClientOptions.
    /// </summary>
    [JsonPropertyName("serviceVersion")]
    public AzureOpenAIServiceVersion? ServiceVersion { get; set; }

    /// <summary>
    /// Optional Entra authentication audience/scope, for example AzureOpenAIAudience.AzureGovernment.ToString().
    /// </summary>
    [JsonPropertyName("audience")]
    public string? Audience { get; set; }

    /// <summary>
    /// Default request headers applied by AzureOpenAIClientOptions.
    /// </summary>
    [JsonPropertyName("defaultHeaders")]
    public Dictionary<string, string>? DefaultHeaders { get; set; }

    /// <summary>
    /// Default query parameters applied by AzureOpenAIClientOptions.
    /// </summary>
    [JsonPropertyName("defaultQueryParameters")]
    public Dictionary<string, string>? DefaultQueryParameters { get; set; }

    /// <summary>
    /// Optional application id appended to the SDK user agent.
    /// </summary>
    [JsonPropertyName("userAgentApplicationId")]
    public string? UserAgentApplicationId { get; set; }

    /// <summary>
    /// Network timeout in milliseconds for the SDK pipeline.
    /// </summary>
    [JsonPropertyName("networkTimeoutMs")]
    public int? NetworkTimeoutMs { get; set; }

    /// <summary>
    /// Enables or disables distributed tracing in the SDK pipeline.
    /// </summary>
    [JsonPropertyName("enableDistributedTracing")]
    public bool? EnableDistributedTracing { get; set; }
}
