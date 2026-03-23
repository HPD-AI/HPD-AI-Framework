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

        var uri = typed?.Uri ?? config.Uri ?? config.ConnectionString
            ?? throw new InvalidOperationException(
                "Memgraph URI is required. Set GraphStoreConfig.Uri, GraphStoreConfig.ConnectionString, " +
                "or MemgraphGraphStoreConfig.Uri in ProviderOptionsJson.");

        var username = typed?.Username ?? config.Username
            ?? throw new InvalidOperationException(
                "Memgraph username is required. Set GraphStoreConfig.Username " +
                "or MemgraphGraphStoreConfig.Username in ProviderOptionsJson.");

        var password = typed?.Password ?? config.Password
            ?? throw new InvalidOperationException(
                "Memgraph password is required. Set GraphStoreConfig.Password " +
                "or MemgraphGraphStoreConfig.Password in ProviderOptionsJson.");

        // Memgraph default database differs from Neo4j's "neo4j" default.
        var database = typed?.Database ?? "memgraph";

        var driver = GraphDatabase.Driver(uri, AuthTokens.Basic(username, password));
        return new MemgraphGraphStore(driver, database);
    }
}
