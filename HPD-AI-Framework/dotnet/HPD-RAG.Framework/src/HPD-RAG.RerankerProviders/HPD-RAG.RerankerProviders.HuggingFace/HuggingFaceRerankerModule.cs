using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.RAG.Core.Providers.Reranker;

namespace HPD.RAG.RerankerProviders.HuggingFace;

/// <summary>
/// Auto-registers the HuggingFace TEI reranker provider on assembly load.
/// </summary>
public static class HuggingFaceRerankerModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        RerankerDiscovery.RegisterRerankerFactory(() => new HuggingFaceRerankerFeatures());

        RerankerDiscovery.RegisterRerankerConfigType<HuggingFaceRerankerConfig>(
            "huggingface",
            json => JsonSerializer.Deserialize(json, HuggingFaceJsonContext.Default.HuggingFaceRerankerConfig),
            config => JsonSerializer.Serialize(config, HuggingFaceJsonContext.Default.HuggingFaceRerankerConfig));
    }
}
