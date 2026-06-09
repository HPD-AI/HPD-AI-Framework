using System.Text.Json.Serialization;

namespace HPD.RAG.EmbeddingProviders.AzureAI;

/// <summary>
/// Azure OpenAI-specific embedding configuration.
///
/// JSON Example (ProviderOptions):
/// <code>
/// {
///   "endpoint": "https://my-resource.openai.azure.com/",
///   "apiKey": "...",
///   "deploymentName": "text-embedding-ada-002"
/// }
/// </code>
/// </summary>
public sealed class AzureAIEmbeddingConfig
{
    /// <summary>
    /// The Azure OpenAI resource endpoint (e.g. https://my-resource.openai.azure.com/).
    /// </summary>
    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; set; }

    /// <summary>
    /// The Azure OpenAI API key.
    /// </summary>
    [JsonPropertyName("apiKey")]
    public string? ApiKey { get; set; }

    /// <summary>
    /// The Azure OpenAI deployment name for the embedding model.
    /// If omitted, EmbeddingConfig.ModelName is used as the deployment name.
    /// </summary>
    [JsonPropertyName("deploymentName")]
    public string? DeploymentName { get; set; }
}
