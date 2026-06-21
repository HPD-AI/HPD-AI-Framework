using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Providers.VectorStore;
using HPD.RAG.VectorStores.Milvus;
using HPD.RAG.VectorStores.Tests.Shared;

namespace HPD.RAG.VectorStores.Tests.Backends;

public sealed class MilvusVectorStoreTests : VectorStoreTestBase
{
    static MilvusVectorStoreTests() => MilvusVectorStoreModule.Initialize();

    // T-055
    [Fact]
    public void ModuleInitializer_RegistersProvider()
    {
        Assert.NotNull(VectorStoreDiscovery.GetProvider("milvus"));
    }

    // T-056
    [Fact]
    public void Initialize_Idempotent()
    {
        MilvusVectorStoreModule.Initialize();
        MilvusVectorStoreModule.Initialize();

        var count = VectorStoreDiscovery.GetRegisteredProviders().Count(k => k == "milvus");
        Assert.Equal(1, count);
    }

    // T-057
    [Fact]
    public void ProviderKey_IsCorrectString()
    {
        var features = VectorStoreDiscovery.GetProvider("milvus")!;
        Assert.Equal("milvus", features.ProviderKey);
    }

    // T-058: MilvusClient constructor takes host/port — does not connect eagerly.
    [Fact]
    public void CreateVectorStore_HappyPath()
    {
        var features = VectorStoreDiscovery.GetProvider("milvus")!;
        // No connection string needed — host defaults to "localhost"
        var config = new VectorStoreConfig { ProviderKey = "milvus" };
        var store = features.CreateVectorStore(config);
        Assert.NotNull(store);
    }

    // T-059: Milvus uses host+port defaults — no required config fields, so skip this test.
    [Fact(Skip = "Milvus uses host/port with defaults (localhost:19530) — missing config is not an error.")]
    public void CreateVectorStore_MissingConfig_Throws() { }

    // T-060
    [Fact]
    public void CreateFilterTranslator_ReturnsNonNull()
    {
        Assert.NotNull(VectorStoreDiscovery.GetProvider("milvus")!.CreateFilterTranslator());
    }

    // T-061: Milvus returns a string expression
    [Fact]
    public void Translate_EqFilter()
    {
        var translator = VectorStoreDiscovery.GetProvider("milvus")!.CreateFilterTranslator();
        var result = translator.Translate(EqFilter("category", "Technical"));
        Assert.NotNull(result);
        Assert.IsType<string>(result);
    }

    // T-062
    [Fact]
    public void Translate_TagPrefix()
    {
        var translator = VectorStoreDiscovery.GetProvider("milvus")!.CreateFilterTranslator();
        var result = translator.Translate(TagFilter("userId", "u123"));
        Assert.NotNull(result);
        var expr = Assert.IsType<string>(result);
        // Tags resolve to tags["userId"] in Milvus
        Assert.Contains("tags", expr);
    }

    // T-063
    [Fact]
    public void Translate_And()
    {
        var translator = VectorStoreDiscovery.GetProvider("milvus")!.CreateFilterTranslator();
        var result = translator.Translate(AndFilter(EqFilter("a", "1"), EqFilter("b", "2")));
        Assert.NotNull(result);
        var expr = Assert.IsType<string>(result);
        Assert.Contains("&&", expr);
    }

    // T-064
    [Fact]
    public void Translate_Or()
    {
        var translator = VectorStoreDiscovery.GetProvider("milvus")!.CreateFilterTranslator();
        var result = translator.Translate(OrFilter(EqFilter("a", "1"), EqFilter("b", "2")));
        Assert.NotNull(result);
        var expr = Assert.IsType<string>(result);
        Assert.Contains("||", expr);
    }

    // T-065
    [Fact]
    public void Translate_Not()
    {
        var translator = VectorStoreDiscovery.GetProvider("milvus")!.CreateFilterTranslator();
        var result = translator.Translate(NotFilter(EqFilter("a", "b")));
        Assert.NotNull(result);
        var expr = Assert.IsType<string>(result);
        Assert.Contains("!", expr);
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
        var translator = VectorStoreDiscovery.GetProvider("milvus")!.CreateFilterTranslator();
        var result = translator.Translate(FilterForOp(op));
        Assert.NotNull(result);
    }

    // T-067
    [Fact]
    public void Translate_Null_ReturnsNull()
    {
        var translator = VectorStoreDiscovery.GetProvider("milvus")!.CreateFilterTranslator();
        Assert.Null(translator.Translate(null!));
    }
}
