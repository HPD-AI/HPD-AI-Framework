using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Providers.VectorStore;
using HPD.RAG.VectorStores.Redis;
using HPD.RAG.VectorStores.Tests.Shared;

namespace HPD.RAG.VectorStores.Tests.Backends;

public sealed class RedisVectorStoreTests : VectorStoreTestBase
{
    static RedisVectorStoreTests() => RedisVectorStoreModule.Initialize();

    // T-055
    [Fact]
    public void ModuleInitializer_RegistersProvider()
    {
        Assert.NotNull(VectorStoreDiscovery.GetProvider("redis"));
    }

    // T-056
    [Fact]
    public void Initialize_Idempotent()
    {
        RedisVectorStoreModule.Initialize();
        RedisVectorStoreModule.Initialize();

        var count = VectorStoreDiscovery.GetRegisteredProviders().Count(k => k == "redis");
        Assert.Equal(1, count);
    }

    // T-057
    [Fact]
    public void ProviderKey_IsCorrectString()
    {
        var features = VectorStoreDiscovery.GetProvider("redis")!;
        Assert.Equal("redis", features.ProviderKey);
    }

    // T-058: Redis ConnectionMultiplexer.Connect() tries to connect immediately — skip.
    [Fact(Skip = "Redis ConnectionMultiplexer.Connect() attempts a real TCP connection at construction — cannot test without a running Redis server.")]
    public void CreateVectorStore_HappyPath() { }

    // T-059
    [Fact]
    public void CreateVectorStore_MissingConnectionString_Throws()
    {
        var features = VectorStoreDiscovery.GetProvider("redis")!;
        var config = new VectorStoreConfig { ProviderKey = "redis" };
        Assert.ThrowsAny<Exception>(() => features.CreateVectorStore(config));
    }

    // T-060
    [Fact]
    public void CreateFilterTranslator_ReturnsNonNull()
    {
        Assert.NotNull(VectorStoreDiscovery.GetProvider("redis")!.CreateFilterTranslator());
    }

    // T-061: Redis returns a RediSearch query string
    [Fact]
    public void Translate_EqFilter()
    {
        var translator = VectorStoreDiscovery.GetProvider("redis")!.CreateFilterTranslator();
        var result = translator.Translate(EqFilter("category", "Technical"));
        Assert.NotNull(result);
        Assert.IsType<string>(result);
    }

    // T-062
    [Fact]
    public void Translate_TagPrefix()
    {
        var translator = VectorStoreDiscovery.GetProvider("redis")!.CreateFilterTranslator();
        var result = translator.Translate(TagFilter("userId", "u123"));
        Assert.NotNull(result);
        var query = Assert.IsType<string>(result);
        Assert.Contains("tags_", query);
    }

    // T-063
    [Fact]
    public void Translate_And()
    {
        var translator = VectorStoreDiscovery.GetProvider("redis")!.CreateFilterTranslator();
        var result = translator.Translate(AndFilter(EqFilter("a", "1"), EqFilter("b", "2")));
        Assert.NotNull(result);
        Assert.IsType<string>(result);
    }

    // T-064
    [Fact]
    public void Translate_Or()
    {
        var translator = VectorStoreDiscovery.GetProvider("redis")!.CreateFilterTranslator();
        var result = translator.Translate(OrFilter(EqFilter("a", "1"), EqFilter("b", "2")));
        Assert.NotNull(result);
        var query = Assert.IsType<string>(result);
        // Redis OR uses " | " separator
        Assert.Contains("|", query);
    }

    // T-065
    [Fact]
    public void Translate_Not()
    {
        var translator = VectorStoreDiscovery.GetProvider("redis")!.CreateFilterTranslator();
        var result = translator.Translate(NotFilter(EqFilter("a", "b")));
        Assert.NotNull(result);
        var query = Assert.IsType<string>(result);
        // Redis NOT uses "-(" prefix
        Assert.Contains("-", query);
    }

    // T-066
    [Theory]
    [InlineData("gt")]
    [InlineData("gte")]
    [InlineData("lt")]
    [InlineData("lte")]
    [InlineData("neq")]
    [InlineData("contains")]
    [InlineData("startswith")]
    public void Translate_AllComparisonOps(string op)
    {
        var translator = VectorStoreDiscovery.GetProvider("redis")!.CreateFilterTranslator();
        var result = translator.Translate(FilterForOp(op));
        Assert.NotNull(result);
    }

    // T-067
    [Fact]
    public void Translate_Null_ReturnsNull()
    {
        var translator = VectorStoreDiscovery.GetProvider("redis")!.CreateFilterTranslator();
        Assert.Null(translator.Translate(null!));
    }
}
