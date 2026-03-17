using Microsoft.Extensions.AI;
using OllamaSharp;
using HPD.RAG.Core.Providers.Embedding;

namespace HPD.RAG.EmbeddingProviders.Ollama;

/// <summary>
/// Ollama embedding provider for HPD.RAG.
/// Connects to a locally-running Ollama server via OllamaSharp.OllamaApiClient,
/// which directly implements IEmbeddingGenerator&lt;string, Embedding&lt;float&gt;&gt;.
/// Config fields used: ModelName (required).
/// Typed config: OllamaEmbeddingConfig.Endpoint (optional, defaults to http://localhost:11434).
/// </summary>
internal sealed class OllamaEmbeddingProviderFeatures : IEmbeddingProviderFeatures
{
    public string ProviderKey => "ollama";
    public string DisplayName => "Ollama (Local)";

    public IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(
        EmbeddingConfig config, IServiceProvider? services = null)
    {
        if (string.IsNullOrWhiteSpace(config.ModelName))
            throw new InvalidOperationException(
                "ModelName is required for the Ollama embedding provider.");

        // Resolve endpoint: typed config > base config > default
        var typedConfig = config.GetTypedConfig<OllamaEmbeddingConfig>();
        string endpoint = typedConfig?.Endpoint
            ?? config.Endpoint
            ?? "http://localhost:11434";

        // OllamaApiClient directly implements IEmbeddingGenerator<string, Embedding<float>>
        var client = new OllamaApiClient(new Uri(endpoint), config.ModelName);
        return (IEmbeddingGenerator<string, Embedding<float>>)client;
    }
}
