using HPD.RAG.Core.Providers.GraphStore;
using Neo4j.Driver;

namespace HPD.RAG.GraphStoreProviders.Memgraph;

/// <summary>
/// Provider descriptor for the Memgraph graph store.
/// Memgraph speaks the Bolt protocol; the Neo4j .NET driver is used as the transport.
/// Registered automatically via <see cref="MemgraphGraphStoreModule"/>.
/// </summary>
internal sealed class MemgraphGraphStoreFeatures : IGraphStoreFeatures
{
    public string ProviderKey => "memgraph";
    public string DisplayName => "Memgraph";

    public IGraphStore CreateGraphStore(GraphStoreConfig config, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var typed = config.GetTypedConfig<MemgraphGraphStoreConfig>();

        var uri = typed?.Uri
            ?? throw new InvalidOperationException(
                "Memgraph URI is required in MemgraphGraphStoreConfig.ProviderOptions.");

        var username = typed?.Username
            ?? throw new InvalidOperationException(
                "Memgraph username is required in MemgraphGraphStoreConfig.ProviderOptions.");

        var password = typed?.Password
            ?? throw new InvalidOperationException(
                "Memgraph password is required in MemgraphGraphStoreConfig.ProviderOptions.");

        // Memgraph default database differs from Neo4j's "neo4j" default.
        var database = typed?.Database ?? "memgraph";

        var driver = GraphDatabase.Driver(uri, AuthTokens.Basic(username, password));
        return new MemgraphGraphStore(driver, database);
    }
}
