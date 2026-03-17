using System.Runtime.CompilerServices;
using HPD.RAG.Core.Providers.Reranker;

namespace HPD.RAG.RerankerProviders.Jina;

/// <summary>
/// Auto-registers the Jina AI reranker provider on assembly load.
/// Jina uses only the base RerankerConfig fields (ApiKey, ModelName, Endpoint),
/// so no typed config registration is needed.
/// </summary>
public static class JinaRerankerModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        RerankerDiscovery.RegisterRerankerFactory(() => new JinaRerankerFeatures());
    }
}
