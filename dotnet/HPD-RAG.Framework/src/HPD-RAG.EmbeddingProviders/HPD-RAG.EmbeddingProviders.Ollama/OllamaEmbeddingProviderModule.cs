using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.RAG.Core.Providers.Embedding;

namespace HPD.RAG.EmbeddingProviders.Ollama;

/// <summary>
/// Auto-discovers and registers the Ollama embedding provider on assembly load.
/// Also registers the typed config for AOT-compatible JSON serialization.
/// </summary>
public static class OllamaEmbeddingProviderModule
{
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    [ModuleInitializer]
#pragma warning restore CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    public static void Initialize()
    {
        EmbeddingDiscovery.RegisterEmbeddingProviderFactory(() => new OllamaEmbeddingProviderFeatures());

        EmbeddingDiscovery.RegisterEmbeddingConfigType<OllamaEmbeddingConfig>(
            "ollama",
            json => JsonSerializer.Deserialize(json, OllamaEmbeddingJsonContext.Default.OllamaEmbeddingConfig),
            cfg => JsonSerializer.Serialize(cfg, OllamaEmbeddingJsonContext.Default.OllamaEmbeddingConfig));
    }
}
