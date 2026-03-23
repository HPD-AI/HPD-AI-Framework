namespace HPD.RAG.Core.Providers.GraphStore;

/// <summary>
/// Core abstraction implemented by every HPD.RAG.GraphStoreProviders.* package.
/// Mirrors IVectorStoreFeatures exactly.
/// </summary>
public interface IGraphStoreFeatures
{
    string ProviderKey { get; }
    string DisplayName { get; }
    IGraphStore CreateGraphStore(GraphStoreConfig config, IServiceProvider? services = null);
}
