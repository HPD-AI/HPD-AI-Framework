using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Providers.VectorStore;
using HPD.RAG.VectorStores.InMemory;
using HPD.RAG.VectorStores.Tests.Shared;

namespace HPD.RAG.VectorStores.Tests.Backends;

public sealed class InMemoryVectorStoreTests : VectorStoreTestBase
{
    // Ensure the module is initialized once for all tests in this class.
    static InMemoryVectorStoreTests() => InMemoryVectorStoreModule.Initialize();

    // -------------------------------------------------------------------------
    // T-055: ModuleInitializer_RegistersProvider
    // -------------------------------------------------------------------------
    [Fact]
    public void ModuleInitializer_RegistersProvider()
    {
        var provider = VectorStoreDiscovery.GetProvider("inmemory");
        Assert.NotNull(provider);
    }

    // -------------------------------------------------------------------------
    // T-056: Initialize_Idempotent
    // -------------------------------------------------------------------------
    [Fact]
    public void Initialize_Idempotent()
    {
        InMemoryVectorStoreModule.Initialize();
        InMemoryVectorStoreModule.Initialize();

        var count = VectorStoreDiscovery.GetRegisteredProviders().Count(k => k == "inmemory");
        Assert.Equal(1, count);
    }

    // -------------------------------------------------------------------------
    // T-057: ProviderKey_IsCorrectString
    // -------------------------------------------------------------------------
    [Fact]
    public void ProviderKey_IsCorrectString()
    {
        var features = VectorStoreDiscovery.GetProvider("inmemory");
        Assert.NotNull(features);
        Assert.Equal("inmemory", features.ProviderKey);
    }

    // -------------------------------------------------------------------------
    // T-058: CreateVectorStore_HappyPath
    // InMemory requires no connection string — any VectorStoreConfig works.
    // -------------------------------------------------------------------------
    [Fact]
    public void CreateVectorStore_HappyPath()
    {
        var features = VectorStoreDiscovery.GetProvider("inmemory")!;
        var config = new VectorStoreConfig { ProviderKey = "inmemory" };
        var store = features.CreateVectorStore(config);
        Assert.NotNull(store);
    }

    // -------------------------------------------------------------------------
    // T-059: CreateVectorStore_MissingConfig — InMemory never throws for missing
    // connection string (it does not need one), so this test is skipped.
    // -------------------------------------------------------------------------
    [Fact(Skip = "InMemory backend requires no connection string — missing config is not an error by design.")]
    public void CreateVectorStore_MissingConfig_Throws() { }

    // -------------------------------------------------------------------------
    // T-060: CreateFilterTranslator_ReturnsNonNull
    // -------------------------------------------------------------------------
    [Fact]
    public void CreateFilterTranslator_ReturnsNonNull()
    {
        var features = VectorStoreDiscovery.GetProvider("inmemory")!;
        var translator = features.CreateFilterTranslator();
        Assert.NotNull(translator);
    }

    // -------------------------------------------------------------------------
    // T-061: Translate_EqFilter — InMemory returns a Func<Dictionary<string,object?>,bool>
    // -------------------------------------------------------------------------
    [Fact]
    public void Translate_EqFilter()
    {
        var translator = VectorStoreDiscovery.GetProvider("inmemory")!.CreateFilterTranslator();
        var result = translator.Translate(EqFilter("category", "Technical"));
        Assert.NotNull(result);
        // Should be a delegate (predicate)
        Assert.True(result is Delegate);
    }

    // -------------------------------------------------------------------------
    // T-062: Translate_TagPrefix
    // -------------------------------------------------------------------------
    [Fact]
    public void Translate_TagPrefix()
    {
        var translator = VectorStoreDiscovery.GetProvider("inmemory")!.CreateFilterTranslator();
        var result = translator.Translate(TagFilter("userId", "u123"));
        Assert.NotNull(result);
        Assert.True(result is Delegate);
    }

    // -------------------------------------------------------------------------
    // T-063: Translate_And
    // -------------------------------------------------------------------------
    [Fact]
    public void Translate_And()
    {
        var translator = VectorStoreDiscovery.GetProvider("inmemory")!.CreateFilterTranslator();
        var result = translator.Translate(AndFilter(EqFilter("a", "1"), EqFilter("b", "2")));
        Assert.NotNull(result);
        Assert.True(result is Delegate);
    }

    // -------------------------------------------------------------------------
    // T-064: Translate_Or
    // -------------------------------------------------------------------------
    [Fact]
    public void Translate_Or()
    {
        var translator = VectorStoreDiscovery.GetProvider("inmemory")!.CreateFilterTranslator();
        var result = translator.Translate(OrFilter(EqFilter("a", "1"), EqFilter("b", "2")));
        Assert.NotNull(result);
        Assert.True(result is Delegate);
    }

    // -------------------------------------------------------------------------
    // T-065: Translate_Not
    // -------------------------------------------------------------------------
    [Fact]
    public void Translate_Not()
    {
        var translator = VectorStoreDiscovery.GetProvider("inmemory")!.CreateFilterTranslator();
        var result = translator.Translate(NotFilter(EqFilter("a", "b")));
        Assert.NotNull(result);
        Assert.True(result is Delegate);
    }

    // -------------------------------------------------------------------------
    // T-066: Translate_AllComparisonOps
    // -------------------------------------------------------------------------
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
        var translator = VectorStoreDiscovery.GetProvider("inmemory")!.CreateFilterTranslator();
        var result = translator.Translate(FilterForOp(op));
        Assert.NotNull(result);
    }

    // -------------------------------------------------------------------------
    // T-067: Translate_Null_ReturnsNull
    // -------------------------------------------------------------------------
    [Fact]
    public void Translate_Null_ReturnsNull()
    {
        var translator = VectorStoreDiscovery.GetProvider("inmemory")!.CreateFilterTranslator();
        var result = translator.Translate(null!);
        Assert.Null(result);
    }
}
