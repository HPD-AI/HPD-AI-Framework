using HPD.RAG.Core.Providers.GraphStore;
using HPD.RAG.GraphStoreProviders.Memgraph;
using Xunit;

namespace HPD.RAG.Providers.Tests.GraphStoreProviders;

public sealed class MemgraphGraphStoreProviderTests
{
    static MemgraphGraphStoreProviderTests()
    {
        MemgraphGraphStoreModule.Initialize();
    }

    // T-078 (Memgraph variant)
    [Fact]
    public void ModuleInitializer_RegistersProvider()
    {
        var provider = GraphStoreDiscovery.GetProvider("memgraph");
        Assert.NotNull(provider);
    }

    // T-079 (Memgraph variant)
    [Fact]
    public void ProviderKey_IsCorrectString()
    {
        var provider = GraphStoreDiscovery.GetProvider("memgraph");
        Assert.NotNull(provider);
        Assert.Equal("memgraph", provider.ProviderKey);
    }

    // T-080 (Memgraph variant) — Memgraph uses the Neo4j Bolt driver; construction
    // creates the driver without actually connecting to the database.
    [Fact]
    public void CreateGraphStore_WithValidConfig_DoesNotThrow()
    {
        var provider = GraphStoreDiscovery.GetProvider("memgraph");
        Assert.NotNull(provider);

        var config = new GraphStoreConfig
        {
            ProviderKey = "memgraph",
            Uri = "bolt://localhost:7687",
            Username = "memgraph",
            Password = "fake-password-for-testing"
        };

        var store = provider.CreateGraphStore(config, null);
        Assert.NotNull(store);
    }

    // T-081 (Memgraph variant) — Missing URI throws
    [Fact]
    public void CreateGraphStore_MissingUri_Throws()
    {
        var provider = GraphStoreDiscovery.GetProvider("memgraph");
        Assert.NotNull(provider);

        var config = new GraphStoreConfig
        {
            ProviderKey = "memgraph"
            // No Uri, ConnectionString, Username, or Password
        };

        var ex = Assert.ThrowsAny<Exception>(() =>
            provider.CreateGraphStore(config, null));

        Assert.True(
            ex is InvalidOperationException || ex is ArgumentException,
            $"Expected InvalidOperationException or ArgumentException, got {ex.GetType().Name}");
    }
}
