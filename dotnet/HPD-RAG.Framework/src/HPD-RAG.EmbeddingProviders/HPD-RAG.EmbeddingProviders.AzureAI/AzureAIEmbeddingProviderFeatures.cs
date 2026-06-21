using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using HPD.RAG.Core.Providers.Embedding;

namespace HPD.RAG.EmbeddingProviders.AzureAI;

/// <summary>
/// Azure OpenAI embedding provider for HPD.RAG.
/// Uses the Azure OpenAI embeddings deployment via AzureOpenAIClient.
///
/// Config fields used: ModelName (fallback deployment name).
/// Typed config: AzureAIEmbeddingConfig for Endpoint + ApiKey + DeploymentName.
/// </summary>
internal sealed class AzureAIEmbeddingProviderFeatures : IEmbeddingProviderFeatures
{
    public string ProviderKey => "azureai";
    public string DisplayName => "Azure OpenAI";

    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(
        EmbeddingConfig config, IServiceProvider? services = null)
    {
        var typedConfig = config.GetTypedConfig<AzureAIEmbeddingConfig>();

        string? endpoint = typedConfig?.Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException(
                "Endpoint is required for the AzureAI embedding provider. " +
                "Set AzureAIEmbeddingConfig.Endpoint in EmbeddingConfig.ProviderOptions.");

        if (string.IsNullOrWhiteSpace(typedConfig.ApiKey))
            throw new InvalidOperationException(
                "ApiKey is required for the AzureAI embedding provider. " +
                "Set AzureAIEmbeddingConfig.ApiKey in EmbeddingConfig.ProviderOptions.");

        string? deploymentName = typedConfig?.DeploymentName ?? config.ModelName;
        if (string.IsNullOrWhiteSpace(deploymentName))
            throw new InvalidOperationException(
                "DeploymentName (or ModelName) is required for the AzureAI embedding provider.");

        var azureClient = new AzureOpenAIClient(
            new Uri(endpoint),
            new AzureKeyCredential(typedConfig.ApiKey));

        return azureClient.GetEmbeddingClient(deploymentName).AsIEmbeddingGenerator();
    }
}
