using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Providers.VectorStore;
using Microsoft.SemanticKernel.Connectors.InMemory;

namespace HPD.RAG.VectorStores.InMemory;

internal sealed class InMemoryVectorStoreFeatures : IVectorStoreFeatures
{
    public string ProviderKey => "inmemory";
    public string DisplayName => "In-Memory";

    public Microsoft.Extensions.VectorData.VectorStore CreateVectorStore(VectorStoreConfig config, IServiceProvider? services = null)
    {
        return new InMemoryVectorStore();
    }

    public IMragFilterTranslator CreateFilterTranslator() => new InMemoryMragFilterTranslator();
}
