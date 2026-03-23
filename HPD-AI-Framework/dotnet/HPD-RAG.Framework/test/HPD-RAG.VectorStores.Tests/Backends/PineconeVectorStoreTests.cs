using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Providers.VectorStore;
using HPD.RAG.VectorStores.Pinecone;
using HPD.RAG.VectorStores.Tests.Shared;

namespace HPD.RAG.VectorStores.Tests.Backends;

public sealed class PineconeVectorStoreTests : VectorStoreTestBase
{
    static PineconeVectorStoreTests() => PineconeVectorStoreModule.Initialize();

    // T-055
    [Fact]
    public void ModuleInitializer_RegistersProvider()
    {
        Assert.NotNull(VectorStoreDiscovery.GetProvider("pinecone"));
    }

    // T-056
    [Fact]
    public void Initialize_Idempotent()
    {
        PineconeVectorStoreModule.Initialize();
        PineconeVectorStoreModule.Initialize();

        var count = VectorStoreDiscovery.GetRegisteredProviders().Count(k => k == "pinecone");
        Assert.Equal(1, count);
    }

    // T-057
    [Fact]
    public void ProviderKey_IsCorrectString()
    {
        var features = VectorStoreDiscovery.GetProvider("pinecone")!;
        Assert.Equal("pinecone", features.ProviderKey);
    }

    // T-058: PineconeClient constructor accepts any string API key without connecting.
    [Fact]
    public void CreateVectorStore_HappyPath()
    {
        var features = VectorStoreDiscovery.GetProvider("pinecone")!;
        var config = new VectorStoreConfig
        {
            ProviderKey = "pinecone",
            ApiKey = "fake-api-key-for-test"
        };
        var store = features.CreateVectorStore(config);
        Assert.NotNull(store);
    }

    // T-059
    [Fact]
    public void CreateVectorStore_MissingApiKey_Throws()
    {
        var features = VectorStoreDiscovery.GetProvider("pinecone")!;
        var config = new VectorStoreConfig { ProviderKey = "pinecone" };
        Assert.ThrowsAny<Exception>(() => features.CreateVectorStore(config));
    }

    // T-060
    [Fact]
    public void CreateFilterTranslator_ReturnsNonNull()
    {
        Assert.NotNull(VectorStoreDiscovery.GetProvider("pinecone")!.CreateFilterTranslator());
    }

    // T-061: Pinecone returns a JSON string containing the field name
    [Fact]
    public void Translate_EqFilter()
    {
        var translator = VectorStoreDiscovery.GetProvider("pinecone")!.CreateFilterTranslator();
        var result = translator.Translate(EqFilter("category", "Technical"));
        Assert.NotNull(result);
        var json = Assert.IsType<string>(result);
        Assert.Contains("category", json);
    }

    // T-062
    [Fact]
    public void Translate_TagPrefix()
    {
        var translator = VectorStoreDiscovery.GetProvider("pinecone")!.CreateFilterTranslator();
        var result = translator.Translate(TagFilter("userId", "u123"));
        Assert.NotNull(result);
        var json = Assert.IsType<string>(result);
        // Tags resolve to "tags.userId"
        Assert.Contains("tags", json);
    }

    // T-063
    [Fact]
    public void Translate_And()
    {
        var translator = VectorStoreDiscovery.GetProvider("pinecone")!.CreateFilterTranslator();
        var result = translator.Translate(AndFilter(EqFilter("a", "1"), EqFilter("b", "2")));
        Assert.NotNull(result);
        var json = Assert.IsType<string>(result);
        Assert.Contains("$and", json);
    }

    // T-064
    [Fact]
    public void Translate_Or()
    {
        var translator = VectorStoreDiscovery.GetProvider("pinecone")!.CreateFilterTranslator();
        var result = translator.Translate(OrFilter(EqFilter("a", "1"), EqFilter("b", "2")));
        Assert.NotNull(result);
        var json = Assert.IsType<string>(result);
        Assert.Contains("$or", json);
    }

    // T-065
    [Fact]
    public void Translate_Not()
    {
        var translator = VectorStoreDiscovery.GetProvider("pinecone")!.CreateFilterTranslator();
        var result = translator.Translate(NotFilter(EqFilter("a", "b")));
        Assert.NotNull(result);
        var json = Assert.IsType<string>(result);
        Assert.Contains("$nor", json);
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
        var translator = VectorStoreDiscovery.GetProvider("pinecone")!.CreateFilterTranslator();
        var result = translator.Translate(FilterForOp(op));
        Assert.NotNull(result);
    }

    // T-067
    [Fact]
    public void Translate_Null_ReturnsNull()
    {
        var translator = VectorStoreDiscovery.GetProvider("pinecone")!.CreateFilterTranslator();
        Assert.Null(translator.Translate(null!));
    }
}
