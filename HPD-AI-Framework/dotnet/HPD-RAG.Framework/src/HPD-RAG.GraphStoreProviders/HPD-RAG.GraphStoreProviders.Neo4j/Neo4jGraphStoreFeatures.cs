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

        var uri = typed?.Uri
            ?? throw new InvalidOperationException(
                "Neo4j URI is required in Neo4jGraphStoreConfig.ProviderOptions.");

        var username = typed?.Username
            ?? throw new InvalidOperationException(
                "Neo4j username is required in Neo4jGraphStoreConfig.ProviderOptions.");

        var password = typed?.Password
            ?? throw new InvalidOperationException(
                "Neo4j password is required in Neo4jGraphStoreConfig.ProviderOptions.");

        var database = typed?.Database ?? "neo4j";

        var driver = GraphDatabase.Driver(uri, AuthTokens.Basic(username, password));
        return new Neo4jGraphStore(driver, database);
    }
}
