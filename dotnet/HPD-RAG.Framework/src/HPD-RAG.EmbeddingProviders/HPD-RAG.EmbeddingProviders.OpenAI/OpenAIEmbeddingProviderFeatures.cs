using Microsoft.Extensions.AI;
using OpenAI;
using HPD.RAG.Core.Providers.Embedding;

namespace HPD.RAG.EmbeddingProviders.OpenAI;

/// <summary>
/// OpenAI embedding provider for HPD.RAG.
/// Uses the OpenAI embeddings API (e.g. text-embedding-3-small, text-embedding-3-large).
/// Config fields used: ModelName (required).
/// Typed config: OpenAIEmbeddingConfig for ApiKey.
/// </summary>
internal sealed class OpenAIEmbeddingProviderFeatures : IEmbeddingProviderFeatures
{
    public string ProviderKey => "openai";
    public string DisplayName => "OpenAI";

    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(
        EmbeddingConfig config, IServiceProvider? services = null)
    {
        var typedConfig = config.GetTypedConfig<OpenAIEmbeddingConfig>();

        if (string.IsNullOrWhiteSpace(typedConfig?.ApiKey))
            throw new InvalidOperationException(
                "ApiKey is required for the OpenAI embedding provider. " +
                "Set OpenAIEmbeddingConfig.ApiKey in EmbeddingConfig.ProviderOptions.");

        if (string.IsNullOrWhiteSpace(config.ModelName))
            throw new InvalidOperationException(
                "ModelName is required for the OpenAI embedding provider.");

        var client = new OpenAIClient(typedConfig.ApiKey);
        return client.GetEmbeddingClient(config.ModelName).AsIEmbeddingGenerator();
    }
}
