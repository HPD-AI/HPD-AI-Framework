using System.Text.Json;
using HPD.RAG.Core.DTOs;
using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Providers.Embedding;
using HPD.RAG.Core.Providers.GraphStore;
using HPD.RAG.Core.Providers.Reranker;
using HPD.RAG.Core.Providers.VectorStore;
using HPD.RAG.Core.Serialization;
using Microsoft.Extensions.VectorData;
using Xunit;

namespace HPD.RAG.Core.Tests;

/// <summary>T-035 through T-044: Discovery registry tests.</summary>
public class DiscoveryRegistryTests
{
    // ─── Stub implementations ─────────────────────────────────────────────────

    private sealed class StubVectorStoreFeatures : IVectorStoreFeatures
    {
        public StubVectorStoreFeatures(string key) => ProviderKey = key;
        public string ProviderKey { get; }
        public string DisplayName => "Stub";
        public Microsoft.Extensions.VectorData.VectorStore CreateVectorStore(VectorStoreConfig config, IServiceProvider? services = null)
            => throw new NotImplementedException();
        public IMragFilterTranslator CreateFilterTranslator() => new StubFilterTranslator();
    }

    private sealed class StubFilterTranslator : IMragFilterTranslator
    {
        public object? Translate(MragFilterNode? node) => node?.Op;
    }

    private sealed class StubEmbeddingFeatures : IEmbeddingProviderFeatures
    {
        public StubEmbeddingFeatures(string key) => ProviderKey = key;
        public string ProviderKey { get; }
        public string DisplayName => "Stub Embedding";
        public Microsoft.Extensions.AI.IEmbeddingGenerator<string, Microsoft.Extensions.AI.Embedding<float>>
            CreateEmbeddingGenerator(EmbeddingConfig config, IServiceProvider? services = null)
            => throw new NotImplementedException();
    }

    private sealed class StubRerankerFeatures : IRerankerFeatures
    {
        public StubRerankerFeatures(string key) => ProviderKey = key;
        public string ProviderKey { get; }
        public string DisplayName => "Stub Reranker";
        public IReranker CreateReranker(RerankerConfig config, IServiceProvider? services = null)
            => throw new NotImplementedException();
    }

    private sealed class StubGraphStoreFeatures : IGraphStoreFeatures
    {
        public StubGraphStoreFeatures(string key) => ProviderKey = key;
        public string ProviderKey { get; }
        public string DisplayName => "Stub Graph";
        public IGraphStore CreateGraphStore(GraphStoreConfig config, IServiceProvider? services = null)
            => throw new NotImplementedException();
    }

    private sealed class StubConfig
    {
        public string? Value { get; set; }
    }

    // ─── VectorStore Discovery ────────────────────────────────────────────────

    // T-035
    [Fact]
    public void VectorStoreDiscovery_GetProvider_ReturnsNull_ForUnknownKey()
    {
        var result = VectorStoreDiscovery.GetProvider("this-key-does-not-exist-" + Guid.NewGuid());
        Assert.Null(result);
    }

    // T-036
    [Fact]
    public void VectorStoreDiscovery_RegisterAndGet_ReturnsCorrectFeatures()
    {
        var key = "test-vs-" + Guid.NewGuid();
        VectorStoreDiscovery.RegisterVectorStoreFactory(() => new StubVectorStoreFeatures(key));

        var features = VectorStoreDiscovery.GetProvider(key);

        Assert.NotNull(features);
        Assert.Equal(key, features.ProviderKey);
    }

    // T-037
    [Fact]
    public void VectorStoreDiscovery_RegisteredProviders_ContainsRegisteredKey()
    {
        var key = "test-vs-" + Guid.NewGuid();
        VectorStoreDiscovery.RegisterVectorStoreFactory(() => new StubVectorStoreFeatures(key));

        var providers = VectorStoreDiscovery.GetRegisteredProviders();

        Assert.Contains(key, providers);
    }

    // T-038
    [Fact]
    public void VectorStoreDiscovery_RegisterConfigType_RoundtripsJson()
    {
        var key = "test-vs-cfg-" + Guid.NewGuid();
        VectorStoreDiscovery.RegisterVectorStoreFactory(() => new StubVectorStoreFeatures(key));
        VectorStoreDiscovery.RegisterVectorStoreConfigType(
            key,
            json => JsonSerializer.Deserialize<StubConfig>(json),
            cfg => JsonSerializer.Serialize(cfg));

        var original = new StubConfig { Value = "hello" };
        var json = JsonSerializer.Serialize(original);

        var config = new VectorStoreConfig
        {
            ProviderKey = key,
            ProviderOptionsJson = json
        };

        var deserialized = config.GetTypedConfig<StubConfig>();
        Assert.NotNull(deserialized);
        Assert.Equal("hello", deserialized.Value);
    }

    // T-039
    [Fact]
    public void VectorStoreDiscovery_GetTypedConfig_ReturnsNull_WhenNoOptionsJson()
    {
        var config = new VectorStoreConfig { ProviderKey = "stub-no-options" };
        var result = config.GetTypedConfig<StubConfig>();
        Assert.Null(result);
    }

    // T-040
    [Fact]
    public void VectorStoreDiscovery_GetTypedConfig_ReturnsDeserializedConfig_WhenOptionsJsonPresent()
    {
        var key = "test-vs-deser-" + Guid.NewGuid();
        VectorStoreDiscovery.RegisterVectorStoreFactory(() => new StubVectorStoreFeatures(key));
        VectorStoreDiscovery.RegisterVectorStoreConfigType(
            key,
            json => JsonSerializer.Deserialize<StubConfig>(json),
            cfg => JsonSerializer.Serialize(cfg));

        var config = new VectorStoreConfig
        {
            ProviderKey = key,
            ProviderOptionsJson = "{\"Value\":\"test-value\"}"
        };

        var result = config.GetTypedConfig<StubConfig>();
        Assert.NotNull(result);
        Assert.Equal("test-value", result.Value);
    }

    // T-041
    [Fact]
    public void VectorStoreDiscovery_MultipleRegistrations_AllReturnCorrectProvider()
    {
        var key1 = "test-vs-multi-1-" + Guid.NewGuid();
        var key2 = "test-vs-multi-2-" + Guid.NewGuid();
        var key3 = "test-vs-multi-3-" + Guid.NewGuid();

        VectorStoreDiscovery.RegisterVectorStoreFactory(() => new StubVectorStoreFeatures(key1));
        VectorStoreDiscovery.RegisterVectorStoreFactory(() => new StubVectorStoreFeatures(key2));
        VectorStoreDiscovery.RegisterVectorStoreFactory(() => new StubVectorStoreFeatures(key3));

        Assert.Equal(key1, VectorStoreDiscovery.GetProvider(key1)?.ProviderKey);
        Assert.Equal(key2, VectorStoreDiscovery.GetProvider(key2)?.ProviderKey);
        Assert.Equal(key3, VectorStoreDiscovery.GetProvider(key3)?.ProviderKey);
    }

    // ─── Embedding Discovery ──────────────────────────────────────────────────

    // T-042
    [Fact]
    public void EmbeddingDiscovery_RegisterAndGet_ReturnsCorrectFeatures()
    {
        var key = "test-emb-" + Guid.NewGuid();
        EmbeddingDiscovery.RegisterEmbeddingProviderFactory(() => new StubEmbeddingFeatures(key));

        var features = EmbeddingDiscovery.GetProvider(key);

        Assert.NotNull(features);
        Assert.Equal(key, features.ProviderKey);
    }

    // ─── Reranker Discovery ───────────────────────────────────────────────────

    // T-043
    [Fact]
    public void RerankerDiscovery_RegisterAndGet_ReturnsCorrectFeatures()
    {
        var key = "test-rer-" + Guid.NewGuid();
        RerankerDiscovery.RegisterRerankerFactory(() => new StubRerankerFeatures(key));

        var features = RerankerDiscovery.GetProvider(key);

        Assert.NotNull(features);
        Assert.Equal(key, features.ProviderKey);
    }

    // ─── GraphStore Discovery ─────────────────────────────────────────────────

    // T-044
    [Fact]
    public void GraphStoreDiscovery_RegisterAndGet_ReturnsCorrectFeatures()
    {
        var key = "test-gs-" + Guid.NewGuid();
        GraphStoreDiscovery.RegisterGraphStoreFactory(() => new StubGraphStoreFeatures(key));

        var features = GraphStoreDiscovery.GetProvider(key);

        Assert.NotNull(features);
        Assert.Equal(key, features.ProviderKey);
    }
}
