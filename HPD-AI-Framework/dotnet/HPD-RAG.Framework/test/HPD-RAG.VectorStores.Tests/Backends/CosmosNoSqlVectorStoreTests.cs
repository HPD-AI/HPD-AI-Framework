using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Providers.VectorStore;
using HPD.RAG.VectorStores.CosmosNoSql;
using HPD.RAG.VectorStores.Tests.Shared;

namespace HPD.RAG.VectorStores.Tests.Backends;

public sealed class CosmosNoSqlVectorStoreTests : VectorStoreTestBase
{
    static CosmosNoSqlVectorStoreTests() => CosmosNoSqlVectorStoreModule.Initialize();

    // T-055
    [Fact]
    public void ModuleInitializer_RegistersProvider()
    {
        Assert.NotNull(VectorStoreDiscovery.GetProvider("cosmos-nosql"));
    }

    // T-056
    [Fact]
    public void Initialize_Idempotent()
    {
        CosmosNoSqlVectorStoreModule.Initialize();
        CosmosNoSqlVectorStoreModule.Initialize();

        var count = VectorStoreDiscovery.GetRegisteredProviders().Count(k => k == "cosmos-nosql");
        Assert.Equal(1, count);
    }

    // T-057
    [Fact]
    public void ProviderKey_IsCorrectString()
    {
        var features = VectorStoreDiscovery.GetProvider("cosmos-nosql")!;
        Assert.Equal("cosmos-nosql", features.ProviderKey);
    }

    // T-058: CosmosClient accepts a connection string without connecting at construction time.
    [Fact]
    public void CreateVectorStore_HappyPath()
    {
        var features = VectorStoreDiscovery.GetProvider("cosmos-nosql")!;
        // A plausible but fake Cosmos connection string — CosmosClient parses but does not connect eagerly.
        var config = new VectorStoreConfig
        {
            ProviderKey = "cosmos-nosql",
            ConnectionString = "AccountEndpoint=https://fake.documents.azure.com:443/;AccountKey=ZmFrZWtleWZha2VrZXlmYWtla2V5ZmFrZWtleWZha2VrZXlmYWtla2V5Zg==;"
        };
        var store = features.CreateVectorStore(config);
        Assert.NotNull(store);
    }

    // T-059
    [Fact]
    public void CreateVectorStore_MissingConnectionStringAndEndpoint_Throws()
    {
        var features = VectorStoreDiscovery.GetProvider("cosmos-nosql")!;
        var config = new VectorStoreConfig { ProviderKey = "cosmos-nosql" };
        Assert.ThrowsAny<Exception>(() => features.CreateVectorStore(config));
    }

    // T-060
    [Fact]
    public void CreateFilterTranslator_ReturnsNonNull()
    {
        Assert.NotNull(VectorStoreDiscovery.GetProvider("cosmos-nosql")!.CreateFilterTranslator());
    }

    // T-061: CosmosNoSql returns CosmosNoSqlFilterResult — Sql must NOT contain the literal value
    [Fact]
    public void Translate_EqFilter()
    {
        var translator = VectorStoreDiscovery.GetProvider("cosmos-nosql")!.CreateFilterTranslator();
        var result = translator.Translate(EqFilter("category", "Technical"));
        Assert.NotNull(result);
        var sql = GetSql(result);
        Assert.DoesNotContain("Technical", sql);
        Assert.Contains("@p", sql);
    }

    // T-062
    [Fact]
    public void Translate_TagPrefix()
    {
        var translator = VectorStoreDiscovery.GetProvider("cosmos-nosql")!.CreateFilterTranslator();
        var result = translator.Translate(TagFilter("userId", "u123"));
        Assert.NotNull(result);
        var sql = GetSql(result);
        Assert.Contains("tags", sql);
    }

    // T-063
    [Fact]
    public void Translate_And()
    {
        var translator = VectorStoreDiscovery.GetProvider("cosmos-nosql")!.CreateFilterTranslator();
        var result = translator.Translate(AndFilter(EqFilter("a", "1"), EqFilter("b", "2")));
        Assert.NotNull(result);
        Assert.Contains("AND", GetSql(result), StringComparison.OrdinalIgnoreCase);
    }

    // T-064
    [Fact]
    public void Translate_Or()
    {
        var translator = VectorStoreDiscovery.GetProvider("cosmos-nosql")!.CreateFilterTranslator();
        var result = translator.Translate(OrFilter(EqFilter("a", "1"), EqFilter("b", "2")));
        Assert.NotNull(result);
        Assert.Contains("OR", GetSql(result), StringComparison.OrdinalIgnoreCase);
    }

    // T-065
    [Fact]
    public void Translate_Not()
    {
        var translator = VectorStoreDiscovery.GetProvider("cosmos-nosql")!.CreateFilterTranslator();
        var result = translator.Translate(NotFilter(EqFilter("a", "b")));
        Assert.NotNull(result);
        Assert.Contains("NOT", GetSql(result), StringComparison.OrdinalIgnoreCase);
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
        var translator = VectorStoreDiscovery.GetProvider("cosmos-nosql")!.CreateFilterTranslator();
        var result = translator.Translate(FilterForOp(op));
        Assert.NotNull(result);
    }

    // T-067
    [Fact]
    public void Translate_Null_ReturnsNull()
    {
        var translator = VectorStoreDiscovery.GetProvider("cosmos-nosql")!.CreateFilterTranslator();
        Assert.Null(translator.Translate(null!));
    }

    private static string GetSql(object result)
    {
        var prop = result.GetType().GetProperty("Sql");
        return (string?)prop?.GetValue(result) ?? string.Empty;
    }
}
