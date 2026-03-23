using HPD.RAG.Core.Providers.GraphStore;
using HPD.RAG.GraphStoreProviders.Neo4j;
using Xunit;

namespace HPD.RAG.Providers.Tests.GraphStoreProviders;

public sealed class Neo4jGraphStoreProviderTests
{
    static Neo4jGraphStoreProviderTests()
    {
        Neo4jGraphStoreModule.Initialize();
    }

    // T-078
    [Fact]
    public void ModuleInitializer_RegistersProvider()
    {
        var provider = GraphStoreDiscovery.GetProvider("neo4j");
        Assert.NotNull(provider);
    }

    // T-079
    [Fact]
    public void ProviderKey_IsCorrectString()
    {
        var provider = GraphStoreDiscovery.GetProvider("neo4j");
        Assert.NotNull(provider);
        Assert.Equal("neo4j", provider.ProviderKey);
    }

    // T-080 — CreateGraphStore with a syntactically valid URI + credentials.
    // The Neo4j driver validates the URI scheme at construction but does NOT connect.
    [Fact]
    public void CreateGraphStore_WithValidConfig_DoesNotThrow()
    {
        var provider = GraphStoreDiscovery.GetProvider("neo4j");
        Assert.NotNull(provider);

        var config = new GraphStoreConfig
        {
            ProviderKey = "neo4j",
            Uri = "bolt://localhost:7687",
            Username = "neo4j",
            Password = "fake-password-for-testing"
        };

        var store = provider.CreateGraphStore(config, null);
        Assert.NotNull(store);
    }

    // T-081 — Missing URI throws
    [Fact]
    public void CreateGraphStore_MissingUri_Throws()
    {
        var provider = GraphStoreDiscovery.GetProvider("neo4j");
        Assert.NotNull(provider);

        var config = new GraphStoreConfig
        {
            ProviderKey = "neo4j"
            // No Uri, ConnectionString, Username, or Password
        };

        var ex = Assert.ThrowsAny<Exception>(() =>
            provider.CreateGraphStore(config, null));

        Assert.True(
            ex is InvalidOperationException || ex is ArgumentException,
            $"Expected InvalidOperationException or ArgumentException, got {ex.GetType().Name}");
    }
}
