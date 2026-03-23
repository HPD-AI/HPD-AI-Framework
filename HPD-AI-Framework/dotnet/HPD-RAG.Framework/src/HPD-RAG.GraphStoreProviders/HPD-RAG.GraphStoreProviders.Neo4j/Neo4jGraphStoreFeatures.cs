using HPD.RAG.Core.Providers.GraphStore;
using Neo4j.Driver;

namespace HPD.RAG.GraphStoreProviders.Neo4j;

/// <summary>
/// Provider descriptor for the Neo4j graph store.
/// Registered automatically via <see cref="Neo4jGraphStoreModule"/>.
/// </summary>
internal sealed class Neo4jGraphStoreFeatures : IGraphStoreFeatures
{
    public string ProviderKey => "neo4j";
    public string DisplayName => "Neo4j";

    public IGraphStore CreateGraphStore(GraphStoreConfig config, IServiceProvider? services = null)
    {
        ArgumentNullException.ThrowIfNull(config);

        var typed = config.GetTypedConfig<Neo4jGraphStoreConfig>();

        var uri = typed?.Uri ?? config.Uri ?? config.ConnectionString
            ?? throw new InvalidOperationException(
                "Neo4j URI is required. Set GraphStoreConfig.Uri, GraphStoreConfig.ConnectionString, " +
                "or Neo4jGraphStoreConfig.Uri in ProviderOptionsJson.");

        var username = typed?.Username ?? config.Username
            ?? throw new InvalidOperationException(
                "Neo4j username is required. Set GraphStoreConfig.Username " +
                "or Neo4jGraphStoreConfig.Username in ProviderOptionsJson.");

        var password = typed?.Password ?? config.Password
            ?? throw new InvalidOperationException(
                "Neo4j password is required. Set GraphStoreConfig.Password " +
                "or Neo4jGraphStoreConfig.Password in ProviderOptionsJson.");

        var database = typed?.Database ?? "neo4j";

        var driver = GraphDatabase.Driver(uri, AuthTokens.Basic(username, password));
        return new Neo4jGraphStore(driver, database);
    }
}
