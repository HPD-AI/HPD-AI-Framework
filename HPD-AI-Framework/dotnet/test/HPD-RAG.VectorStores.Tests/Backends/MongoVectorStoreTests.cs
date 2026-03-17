using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Providers.VectorStore;
using HPD.RAG.VectorStores.Mongo;
using HPD.RAG.VectorStores.Tests.Shared;
using MongoDB.Bson;

namespace HPD.RAG.VectorStores.Tests.Backends;

public sealed class MongoVectorStoreTests : VectorStoreTestBase
{
    static MongoVectorStoreTests() => MongoVectorStoreModule.Initialize();

    // T-055
    [Fact]
    public void ModuleInitializer_RegistersProvider()
    {
        Assert.NotNull(VectorStoreDiscovery.GetProvider("mongo"));
    }

    // T-056
    [Fact]
    public void Initialize_Idempotent()
    {
        MongoVectorStoreModule.Initialize();
        MongoVectorStoreModule.Initialize();

        var count = VectorStoreDiscovery.GetRegisteredProviders().Count(k => k == "mongo");
        Assert.Equal(1, count);
    }

    // T-057
    [Fact]
    public void ProviderKey_IsCorrectString()
    {
        var features = VectorStoreDiscovery.GetProvider("mongo")!;
        Assert.Equal("mongo", features.ProviderKey);
    }

    // T-058: MongoClient constructor accepts any connection string without connecting immediately.
    [Fact]
    public void CreateVectorStore_HappyPath()
    {
        var features = VectorStoreDiscovery.GetProvider("mongo")!;
        var config = new VectorStoreConfig
        {
            ProviderKey = "mongo",
            ConnectionString = "mongodb://localhost:27017"
        };
        var store = features.CreateVectorStore(config);
        Assert.NotNull(store);
    }

    // T-059
    [Fact]
    public void CreateVectorStore_MissingConnectionString_Throws()
    {
        var features = VectorStoreDiscovery.GetProvider("mongo")!;
        var config = new VectorStoreConfig { ProviderKey = "mongo" };
        Assert.ThrowsAny<Exception>(() => features.CreateVectorStore(config));
    }

    // T-060
    [Fact]
    public void CreateFilterTranslator_ReturnsNonNull()
    {
        Assert.NotNull(VectorStoreDiscovery.GetProvider("mongo")!.CreateFilterTranslator());
    }

    // T-061: Mongo returns a BsonDocument
    [Fact]
    public void Translate_EqFilter()
    {
        var translator = VectorStoreDiscovery.GetProvider("mongo")!.CreateFilterTranslator();
        var result = translator.Translate(EqFilter("category", "Technical"));
        Assert.NotNull(result);
        Assert.IsType<BsonDocument>(result);
    }

    // T-062
    [Fact]
    public void Translate_TagPrefix()
    {
        var translator = VectorStoreDiscovery.GetProvider("mongo")!.CreateFilterTranslator();
        var result = translator.Translate(TagFilter("userId", "u123"));
        Assert.NotNull(result);
        var doc = Assert.IsType<BsonDocument>(result);
        // Field resolves to "tags.userId"
        Assert.True(doc.Contains("tags.userId"));
    }

    // T-063
    [Fact]
    public void Translate_And()
    {
        var translator = VectorStoreDiscovery.GetProvider("mongo")!.CreateFilterTranslator();
        var result = translator.Translate(AndFilter(EqFilter("a", "1"), EqFilter("b", "2")));
        Assert.NotNull(result);
        var doc = Assert.IsType<BsonDocument>(result);
        Assert.True(doc.Contains("$and"));
    }

    // T-064
    [Fact]
    public void Translate_Or()
    {
        var translator = VectorStoreDiscovery.GetProvider("mongo")!.CreateFilterTranslator();
        var result = translator.Translate(OrFilter(EqFilter("a", "1"), EqFilter("b", "2")));
        Assert.NotNull(result);
        var doc = Assert.IsType<BsonDocument>(result);
        Assert.True(doc.Contains("$or"));
    }

    // T-065
    [Fact]
    public void Translate_Not()
    {
        var translator = VectorStoreDiscovery.GetProvider("mongo")!.CreateFilterTranslator();
        var result = translator.Translate(NotFilter(EqFilter("a", "b")));
        Assert.NotNull(result);
        var doc = Assert.IsType<BsonDocument>(result);
        Assert.True(doc.Contains("$nor"));
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
        var translator = VectorStoreDiscovery.GetProvider("mongo")!.CreateFilterTranslator();
        var result = translator.Translate(FilterForOp(op));
        Assert.NotNull(result);
    }

    // T-067
    [Fact]
    public void Translate_Null_ReturnsNull()
    {
        var translator = VectorStoreDiscovery.GetProvider("mongo")!.CreateFilterTranslator();
        Assert.Null(translator.Translate(null!));
    }
}
