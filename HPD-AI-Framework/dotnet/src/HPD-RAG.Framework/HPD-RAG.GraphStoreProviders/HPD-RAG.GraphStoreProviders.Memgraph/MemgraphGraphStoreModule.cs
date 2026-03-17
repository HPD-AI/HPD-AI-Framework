using System.Runtime.CompilerServices;
using System.Text.Json;
using HPD.RAG.Core.Providers.GraphStore;

namespace HPD.RAG.GraphStoreProviders.Memgraph;

/// <summary>
/// Auto-discovers and registers the Memgraph graph store provider on assembly load.
/// Also registers the typed config for AOT-compatible JSON serialization.
/// </summary>
public static class MemgraphGraphStoreModule
{
#pragma warning disable CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    [ModuleInitializer]
#pragma warning restore CA2255 // The 'ModuleInitializer' attribute should not be used in libraries
    public static void Initialize()
    {
        GraphStoreDiscovery.RegisterGraphStoreFactory(() => new MemgraphGraphStoreFeatures());

        GraphStoreDiscovery.RegisterGraphStoreConfigType<MemgraphGraphStoreConfig>(
            "memgraph",
            json => JsonSerializer.Deserialize(json, MemgraphGraphStoreJsonContext.Default.MemgraphGraphStoreConfig),
            cfg => JsonSerializer.Serialize(cfg, MemgraphGraphStoreJsonContext.Default.MemgraphGraphStoreConfig));
    }
}
