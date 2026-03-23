using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Providers.VectorStore;
using HPD.RAG.VectorStores.Tests.Shared;
using HPD.RAG.VectorStores.Weaviate;

namespace HPD.RAG.VectorStores.Tests.Backends;

public sealed class WeaviateVectorStoreTests : VectorStoreTestBase
{
    static WeaviateVectorStoreTests() => WeaviateVectorStoreModule.Initialize();

    // T-055
    [Fact]
    public void ModuleInitializer_RegistersProvider()
    {
        Assert.NotNull(VectorStoreDiscovery.GetProvider("weaviate"));
    }

    // T-056
    [Fact]
    public void Initialize_Idempotent()
    {
        WeaviateVectorStoreModule.Initialize();
        WeaviateVectorStoreModule.Initialize();

        var count = VectorStoreDiscovery.GetRegisteredProviders().Count(k => k == "weaviate");
        Assert.Equal(1, count);
    }

    // T-057
    [Fact]
    public void ProviderKey_IsCorrectString()
    {
        var features = VectorStoreDiscovery.GetProvider("weaviate")!;
        Assert.Equal("weaviate", features.ProviderKey);
    }

    // T-058: Weaviate sets BaseAddress on an HttpClient — does not connect at construction.
    [Fact]
    public void CreateVectorStore_HappyPath()
    {
        var features = VectorStoreDiscovery.GetProvider("weaviate")!;
        var config = new VectorStoreConfig
        {
            ProviderKey = "weaviate",
            Endpoint = "http://localhost:8080"
        };
        var store = features.CreateVectorStore(config);
        Assert.NotNull(store);
    }

    // T-059
    [Fact]
    public void CreateVectorStore_MissingEndpoint_Throws()
    {
        var features = VectorStoreDiscovery.GetProvider("weaviate")!;
        var config = new VectorStoreConfig { ProviderKey = "weaviate" };
        Assert.ThrowsAny<Exception>(() => features.CreateVectorStore(config));
    }

    // T-060
    [Fact]
    public void CreateFilterTranslator_ReturnsNonNull()
    {
        Assert.NotNull(VectorStoreDiscovery.GetProvider("weaviate")!.CreateFilterTranslator());
    }

    // T-061: Weaviate returns WeaviateWhereOperand
    [Fact]
    public void Translate_EqFilter()
    {
        var translator = VectorStoreDiscovery.GetProvider("weaviate")!.CreateFilterTranslator();
        var result = translator.Translate(EqFilter("category", "Technical"));
        Assert.NotNull(result);
        Assert.IsType<WeaviateWhereOperand>(result);
    }

    // T-062
    [Fact]
    public void Translate_TagPrefix()
    {
        var translator = VectorStoreDiscovery.GetProvider("weaviate")!.CreateFilterTranslator();
        var result = translator.Translate(TagFilter("userId", "u123"));
        Assert.NotNull(result);
        // Tags resolve to "tags_userId" field in Weaviate operand
        var operand = (WeaviateWhereOperand)result;
        Assert.Contains("tags_", operand.Path?[0] ?? string.Empty);
    }

    // T-063
    [Fact]
    public void Translate_And()
    {
        var translator = VectorStoreDiscovery.GetProvider("weaviate")!.CreateFilterTranslator();
        var result = translator.Translate(AndFilter(EqFilter("a", "1"), EqFilter("b", "2")));
        Assert.NotNull(result);
        var operand = (WeaviateWhereOperand)result;
        Assert.Equal("And", operand.Operator);
    }

    // T-064
    [Fact]
    public void Translate_Or()
    {
        var translator = VectorStoreDiscovery.GetProvider("weaviate")!.CreateFilterTranslator();
        var result = translator.Translate(OrFilter(EqFilter("a", "1"), EqFilter("b", "2")));
        Assert.NotNull(result);
        var operand = (WeaviateWhereOperand)result;
        Assert.Equal("Or", operand.Operator);
    }

    // T-065
    [Fact]
    public void Translate_Not()
    {
        var translator = VectorStoreDiscovery.GetProvider("weaviate")!.CreateFilterTranslator();
        var result = translator.Translate(NotFilter(EqFilter("a", "b")));
        Assert.NotNull(result);
        var operand = (WeaviateWhereOperand)result;
        Assert.Equal("Not", operand.Operator);
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
        var translator = VectorStoreDiscovery.GetProvider("weaviate")!.CreateFilterTranslator();
        var result = translator.Translate(FilterForOp(op));
        Assert.NotNull(result);
    }

    // T-067
    [Fact]
    public void Translate_Null_ReturnsNull()
    {
        var translator = VectorStoreDiscovery.GetProvider("weaviate")!.CreateFilterTranslator();
        Assert.Null(translator.Translate(null!));
    }
}
