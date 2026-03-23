using HPD.RAG.Core.Providers.Embedding;
using HPD.RAG.Core.Providers.VectorStore;
using HPD.RAG.VectorStores.InMemory;
using Xunit;

namespace HPD.RAG.IntegrationTests.Tests;

/// <summary>
/// Group 6: Provider discovery tests — verify that the InMemory vector store is registered
/// via [ModuleInitializer] when the assembly is loaded, and that VectorStoreDiscovery and
/// EmbeddingDiscovery behave correctly for known and unknown keys.
///
/// Each test calls InMemoryVectorStoreModule.Initialize() explicitly because [ModuleInitializer]
/// timing in xUnit is not guaranteed — the static dictionary must be populated before any
/// assertion against it.
/// </summary>
public sealed class MragProviderDiscoveryTests
{
    private static void EnsureInMemoryRegistered()
        => InMemoryVectorStoreModule.Initialize();

    // T-059
    [Fact]
    public void VectorStoreDiscovery_InMemory_IsRegistered()
    {
        EnsureInMemoryRegistered();
        var provider = VectorStoreDiscovery.GetProvider("inmemory");
        Assert.NotNull(provider);
        Assert.Equal("inmemory", provider.ProviderKey);
    }

    // T-060
    [Fact]
    public void EmbeddingDiscovery_GetRegisteredProviders_ReturnsCollection()
    {
        var providers = EmbeddingDiscovery.GetRegisteredProviders();
        Assert.NotNull(providers);
    }

    // T-061
    [Fact]
    public void VectorStoreDiscovery_UnknownKey_ReturnsNull()
    {
        var provider = VectorStoreDiscovery.GetProvider("__nonexistent_provider_key__");
        Assert.Null(provider);
    }

    // T-062
    [Fact]
    public void VectorStoreDiscovery_InMemory_CanCreateVectorStore()
    {
        EnsureInMemoryRegistered();
        var provider = VectorStoreDiscovery.GetProvider("inmemory");
        Assert.NotNull(provider);

        var config = new VectorStoreConfig { ProviderKey = "inmemory" };
        var store = provider.CreateVectorStore(config);
        Assert.NotNull(store);
    }

    // T-063
    [Fact]
    public void VectorStoreDiscovery_InMemory_CanCreateFilterTranslator()
    {
        EnsureInMemoryRegistered();
        var provider = VectorStoreDiscovery.GetProvider("inmemory");
        Assert.NotNull(provider);

        var translator = provider.CreateFilterTranslator();
        Assert.NotNull(translator);
    }

    // T-064
    [Fact]
    public void VectorStoreDiscovery_GetRegisteredProviders_ContainsInMemory()
    {
        EnsureInMemoryRegistered();
        var providers = VectorStoreDiscovery.GetRegisteredProviders();
        Assert.Contains("inmemory", providers);
    }
}
