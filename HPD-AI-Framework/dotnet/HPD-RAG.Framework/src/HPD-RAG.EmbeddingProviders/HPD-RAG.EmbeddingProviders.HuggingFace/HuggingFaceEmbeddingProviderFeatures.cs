using System.Net.Http.Headers;
using Microsoft.Extensions.AI;
using HPD.RAG.Core.Providers.Embedding;

namespace HPD.RAG.EmbeddingProviders.HuggingFace;

/// <summary>
/// HuggingFace Inference API embedding provider for HPD.RAG.
/// Uses the HuggingFace feature-extraction endpoint via HttpClient and the
/// Microsoft.Extensions.AI IEmbeddingGenerator contract.
///
/// Config fields used: ModelName (required), ApiKey (required or via typed config).
/// Typed config: HuggingFaceEmbeddingConfig for Endpoint + ApiKey overrides.
/// </summary>
internal sealed class HuggingFaceEmbeddingProviderFeatures : IEmbeddingProviderFeatures
{
    public string ProviderKey => "huggingface";
    public string DisplayName => "HuggingFace Inference API";

    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(
        EmbeddingConfig config, IServiceProvider? services = null)
    {
        if (string.IsNullOrWhiteSpace(config.ModelName))
            throw new InvalidOperationException(
                "ModelName is required for the HuggingFace embedding provider.");

        var typedConfig = config.GetTypedConfig<HuggingFaceEmbeddingConfig>();

        // Resolve API key: typed config > base config
        string? apiKey = typedConfig?.ApiKey ?? config.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "ApiKey is required for the HuggingFace embedding provider. " +
                "Set it via EmbeddingConfig.ApiKey or HuggingFaceEmbeddingConfig.ApiKey.");

        // Resolve endpoint: typed config > base config > default
        string baseEndpoint = typedConfig?.Endpoint
            ?? config.Endpoint
            ?? "https://api-inference.huggingface.co";

        // Build an HttpClient targeting the feature-extraction endpoint for the given model
        // POST https://api-inference.huggingface.co/pipeline/feature-extraction/{model}
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri($"{baseEndpoint.TrimEnd('/')}/pipeline/feature-extraction/{config.ModelName}")
        };
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);

        return new HuggingFaceEmbeddingGenerator(httpClient, config.ModelName);
    }
}
