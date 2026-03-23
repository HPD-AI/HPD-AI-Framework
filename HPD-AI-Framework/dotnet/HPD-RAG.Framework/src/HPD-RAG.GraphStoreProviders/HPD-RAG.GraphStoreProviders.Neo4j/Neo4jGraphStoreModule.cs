using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.RAG.Core.Providers.GraphStore;

namespace HPD.RAG.GraphStoreProviders.Neo4j;

/// <summary>
/// Auto-discovers and registers the Neo4j graph store provider on assembly load.
/// Also registers the typed config for AOT-compatible JSON serialization.
/// </summary>
public static class Neo4jGraphStoreModule
{
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    [ModuleInitializer]
#pragma warning restore CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    public static void Initialize()
    {
        GraphStoreDiscovery.RegisterGraphStoreFactory(() => new Neo4jGraphStoreFeatures());

        GraphStoreDiscovery.RegisterGraphStoreConfigType<Neo4jGraphStoreConfig>(
            "neo4j",
            json => JsonSerializer.Deserialize(json, Neo4jGraphStoreJsonContext.Default.Neo4jGraphStoreConfig),
            cfg => JsonSerializer.Serialize(cfg, Neo4jGraphStoreJsonContext.Default.Neo4jGraphStoreConfig));
    }
}
