using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.RAG.Core.Providers.Reranker;

namespace HPD.RAG.RerankerProviders.OnnxRuntime;

/// <summary>
/// Auto-registers the ONNX Runtime reranker provider on assembly load.
/// </summary>
public static class OnnxRuntimeRerankerModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        RerankerDiscovery.RegisterRerankerFactory(() => new OnnxRuntimeRerankerFeatures());

        RerankerDiscovery.RegisterRerankerConfigType<OnnxRuntimeRerankerConfig>(
            "onnxruntime",
            json => JsonSerializer.Deserialize(json, OnnxRuntimeJsonContext.Default.OnnxRuntimeRerankerConfig),
            config => JsonSerializer.Serialize(config, OnnxRuntimeJsonContext.Default.OnnxRuntimeRerankerConfig));
    }
}
