using HPD.RAG.Core.Filters;
using Microsoft.Extensions.VectorData;

namespace HPD.RAG.Core.Providers.VectorStore;

/// <summary>
/// Core abstraction implemented by every HPD.RAG.VectorStores.* package.
/// Mirrors IProviderFeatures from the HPD Agent provider system exactly.
/// Each package self-registers via [ModuleInitializer] — no manual registration required.
/// </summary>
public interface IVectorStoreFeatures
{
    string ProviderKey { get; }
    string DisplayName { get; }

    Microsoft.Extensions.VectorData.VectorStore CreateVectorStore(VectorStoreConfig config, IServiceProvider? services = null);

    /// <summary>
    /// Returns the backend-specific MragFilterNode → native syntax compiler.
    /// Called at execution time by VectorSearchHandler and HybridSearchHandler.
    /// </summary>
    IMragFilterTranslator CreateFilterTranslator();
}
