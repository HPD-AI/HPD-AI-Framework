using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Providers.VectorStore;
using HPD.RAG.VectorStores.Qdrant;
using HPD.RAG.VectorStores.Tests.Shared;
using QdrantFilter = Qdrant.Client.Grpc.Filter;

namespace HPD.RAG.VectorStores.Tests.Backends;

public sealed class QdrantVectorStoreTests : VectorStoreTestBase
{
    static QdrantVectorStoreTests() => QdrantVectorStoreModule.Initialize();

    // T-055
    [Fact]
    public void ModuleInitializer_RegistersProvider()
    {
        var provider = VectorStoreDiscovery.GetProvider("qdrant");
        Assert.NotNull(provider);
    }

    // T-056
    [Fact]
    public void Initialize_Idempotent()
    {
        QdrantVectorStoreModule.Initialize();
        QdrantVectorStoreModule.Initialize();

        var count = VectorStoreDiscovery.GetRegisteredProviders().Count(k => k == "qdrant");
        Assert.Equal(1, count);
    }

    // T-057
    [Fact]
    public void ProviderKey_IsCorrectString()
    {
        var features = VectorStoreDiscovery.GetProvider("qdrant");
        Assert.NotNull(features);
        Assert.Equal("qdrant", features.ProviderKey);
    }

    // T-058: Qdrant client constructor connects lazily; creating with a fake host does not throw.
    [Fact]
    public void CreateVectorStore_HappyPath()
    {
        var features = VectorStoreDiscovery.GetProvider("qdrant")!;
        var config = new VectorStoreConfig
        {
            ProviderKey = "qdrant",
            Endpoint = "http://localhost:6333"
        };
        var store = features.CreateVectorStore(config);
        Assert.NotNull(store);
    }

    // T-059
    [Fact]
    public void CreateVectorStore_MissingEndpoint_Throws()
    {
        var features = VectorStoreDiscovery.GetProvider("qdrant")!;
        var config = new VectorStoreConfig { ProviderKey = "qdrant" };
        var ex = Assert.ThrowsAny<Exception>(() => features.CreateVectorStore(config));
        Assert.NotNull(ex);
    }

    // T-060
    [Fact]
    public void CreateFilterTranslator_ReturnsNonNull()
    {
        var features = VectorStoreDiscovery.GetProvider("qdrant")!;
        Assert.NotNull(features.CreateFilterTranslator());
    }

    // T-061: Qdrant returns a QdrantFilter (Grpc type)
    [Fact]
    public void Translate_EqFilter()
    {
        var translator = VectorStoreDiscovery.GetProvider("qdrant")!.CreateFilterTranslator();
        var result = translator.Translate(EqFilter("category", "Technical"));
        Assert.NotNull(result);
        Assert.IsType<QdrantFilter>(result);
    }

    // T-062
    [Fact]
    public void Translate_TagPrefix()
    {
        var translator = VectorStoreDiscovery.GetProvider("qdrant")!.CreateFilterTranslator();
        var result = translator.Translate(TagFilter("userId", "u123"));
        Assert.NotNull(result);
        Assert.IsType<QdrantFilter>(result);
    }

    // T-063
    [Fact]
    public void Translate_And()
    {
        var translator = VectorStoreDiscovery.GetProvider("qdrant")!.CreateFilterTranslator();
        var result = translator.Translate(AndFilter(EqFilter("a", "1"), EqFilter("b", "2")));
        Assert.NotNull(result);
        Assert.IsType<QdrantFilter>(result);
    }

    // T-064
    [Fact]
    public void Translate_Or()
    {
        var translator = VectorStoreDiscovery.GetProvider("qdrant")!.CreateFilterTranslator();
        var result = translator.Translate(OrFilter(EqFilter("a", "1"), EqFilter("b", "2")));
        Assert.NotNull(result);
        Assert.IsType<QdrantFilter>(result);
    }

    // T-065
    [Fact]
    public void Translate_Not()
    {
        var translator = VectorStoreDiscovery.GetProvider("qdrant")!.CreateFilterTranslator();
        var result = translator.Translate(NotFilter(EqFilter("a", "b")));
        Assert.NotNull(result);
        Assert.IsType<QdrantFilter>(result);
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
        var translator = VectorStoreDiscovery.GetProvider("qdrant")!.CreateFilterTranslator();
        var result = translator.Translate(FilterForOp(op));
        Assert.NotNull(result);
    }

    // T-067
    [Fact]
    public void Translate_Null_ReturnsNull()
    {
        var translator = VectorStoreDiscovery.GetProvider("qdrant")!.CreateFilterTranslator();
        Assert.Null(translator.Translate(null!));
    }
}
