using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.RAG.Core.Providers.Embedding;

namespace HPD.RAG.EmbeddingProviders.HuggingFace;

/// <summary>
/// Auto-discovers and registers the HuggingFace embedding provider on assembly load.
/// Also registers the typed config for AOT-compatible JSON serialization.
/// </summary>
public static class HuggingFaceEmbeddingProviderModule
{
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    [ModuleInitializer]
#pragma warning restore CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    public static void Initialize()
    {
        EmbeddingDiscovery.RegisterEmbeddingProviderFactory(() => new HuggingFaceEmbeddingProviderFeatures());

        EmbeddingDiscovery.RegisterEmbeddingConfigType<HuggingFaceEmbeddingConfig>(
            "huggingface",
            json => JsonSerializer.Deserialize(json, HuggingFaceEmbeddingJsonContext.Default.HuggingFaceEmbeddingConfig),
            cfg => JsonSerializer.Serialize(cfg, HuggingFaceEmbeddingJsonContext.Default.HuggingFaceEmbeddingConfig));
    }
}
