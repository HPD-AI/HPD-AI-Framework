using System.Runtime.CompilerServices;
using HPD.RAG.Core.Providers.VectorStore;

namespace HPD.RAG.VectorStores.InMemory;

/// <summary>
/// Self-registers the InMemory vector store provider on assembly load.
/// No typed config is registered — InMemory requires no connection string or API key.
/// </summary>
public static class InMemoryVectorStoreModule
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    public static void Initialize()
    {
        VectorStoreDiscovery.RegisterVectorStoreFactory(() => new InMemoryVectorStoreFeatures());
    }
}
