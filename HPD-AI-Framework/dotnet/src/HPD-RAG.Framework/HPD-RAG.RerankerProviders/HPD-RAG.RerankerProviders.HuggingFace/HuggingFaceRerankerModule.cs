using System.Runtime.CompilerServices;
using HPD.RAG.Core.Providers.Reranker;

namespace HPD.RAG.RerankerProviders.HuggingFace;

/// <summary>
/// Auto-registers the HuggingFace TEI reranker provider on assembly load.
/// HuggingFace TEI has no provider-specific typed config beyond the base RerankerConfig fields
/// (Endpoint and ApiKey), so no config type registration is needed.
/// </summary>
public static class HuggingFaceRerankerModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        RerankerDiscovery.RegisterRerankerFactory(() => new HuggingFaceRerankerFeatures());
    }
}
