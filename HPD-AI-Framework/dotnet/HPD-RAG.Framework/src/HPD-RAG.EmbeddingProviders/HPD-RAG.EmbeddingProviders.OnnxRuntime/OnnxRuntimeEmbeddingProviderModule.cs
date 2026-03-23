using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.RAG.Core.Providers.Embedding;

namespace HPD.RAG.EmbeddingProviders.OnnxRuntime;

/// <summary>
/// Auto-discovers and registers the OnnxRuntime embedding provider on assembly load.
/// Also registers the typed config for AOT-compatible JSON serialization.
/// </summary>
public static class OnnxRuntimeEmbeddingProviderModule
{
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    [ModuleInitializer]
#pragma warning restore CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    public static void Initialize()
    {
        EmbeddingDiscovery.RegisterEmbeddingProviderFactory(() => new OnnxRuntimeEmbeddingProviderFeatures());

        EmbeddingDiscovery.RegisterEmbeddingConfigType<OnnxRuntimeEmbeddingConfig>(
            "onnxruntime",
            json => JsonSerializer.Deserialize(json, OnnxRuntimeEmbeddingJsonContext.Default.OnnxRuntimeEmbeddingConfig),
            cfg => JsonSerializer.Serialize(cfg, OnnxRuntimeEmbeddingJsonContext.Default.OnnxRuntimeEmbeddingConfig));
    }
}
