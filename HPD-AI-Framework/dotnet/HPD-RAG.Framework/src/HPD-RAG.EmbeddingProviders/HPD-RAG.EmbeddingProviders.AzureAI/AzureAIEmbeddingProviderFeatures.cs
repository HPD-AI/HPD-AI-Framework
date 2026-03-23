using Azure;
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using HPD.RAG.Core.Providers.Embedding;

namespace HPD.RAG.EmbeddingProviders.AzureAI;

/// <summary>
/// Azure OpenAI embedding provider for HPD.RAG.
/// Uses the Azure OpenAI embeddings deployment via AzureOpenAIClient.
///
/// Config fields used: ApiKey (required), Endpoint (required or via typed config).
/// Typed config: AzureAIEmbeddingConfig for Endpoint + DeploymentName overrides.
/// </summary>
internal sealed class AzureAIEmbeddingProviderFeatures : IEmbeddingProviderFeatures
{
    public string ProviderKey => "azureai";
    public string DisplayName => "Azure OpenAI";

    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(
        EmbeddingConfig config, IServiceProvider? services = null)
    {
        var typedConfig = config.GetTypedConfig<AzureAIEmbeddingConfig>();

        // Resolve endpoint: typed config > base config
        string? endpoint = typedConfig?.Endpoint ?? config.Endpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException(
                "Endpoint is required for the AzureAI embedding provider. " +
                "Set it via EmbeddingConfig.Endpoint or AzureAIEmbeddingConfig.Endpoint.");

        if (string.IsNullOrWhiteSpace(config.ApiKey))
            throw new InvalidOperationException(
                "ApiKey is required for the AzureAI embedding provider.");

        // Resolve deployment name: typed config > base ModelName
        string? deploymentName = typedConfig?.DeploymentName ?? config.ModelName;
        if (string.IsNullOrWhiteSpace(deploymentName))
            throw new InvalidOperationException(
                "DeploymentName (or ModelName) is required for the AzureAI embedding provider.");

        var azureClient = new AzureOpenAIClient(
            new Uri(endpoint),
            new AzureKeyCredential(config.ApiKey));

        return azureClient.GetEmbeddingClient(deploymentName).AsIEmbeddingGenerator();
    }
}
