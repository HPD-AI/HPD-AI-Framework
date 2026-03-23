using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Providers.VectorStore;
using HPD.RAG.VectorStores.Sqlite;
using HPD.RAG.VectorStores.Tests.Shared;

namespace HPD.RAG.VectorStores.Tests.Backends;

public sealed class SqliteVectorStoreTests : VectorStoreTestBase
{
    static SqliteVectorStoreTests() => SqliteVectorStoreModule.Initialize();

    // T-055
    [Fact]
    public void ModuleInitializer_RegistersProvider()
    {
        Assert.NotNull(VectorStoreDiscovery.GetProvider("sqlite"));
    }

    // T-056
    [Fact]
    public void Initialize_Idempotent()
    {
        SqliteVectorStoreModule.Initialize();
        SqliteVectorStoreModule.Initialize();

        var count = VectorStoreDiscovery.GetRegisteredProviders().Count(k => k == "sqlite");
        Assert.Equal(1, count);
    }

    // T-057
    [Fact]
    public void ProviderKey_IsCorrectString()
    {
        var features = VectorStoreDiscovery.GetProvider("sqlite")!;
        Assert.Equal("sqlite", features.ProviderKey);
    }

    // T-058: SQLite in-memory database — uses ":memory:" which is valid and never touches disk.
    [Fact]
    public void CreateVectorStore_HappyPath()
    {
        var features = VectorStoreDiscovery.GetProvider("sqlite")!;
        var config = new VectorStoreConfig
        {
            ProviderKey = "sqlite",
            ConnectionString = ":memory:"
        };
        var store = features.CreateVectorStore(config);
        Assert.NotNull(store);
    }

    // T-059
    [Fact]
    public void CreateVectorStore_MissingDatabasePath_Throws()
    {
        var features = VectorStoreDiscovery.GetProvider("sqlite")!;
        var config = new VectorStoreConfig { ProviderKey = "sqlite" };
        Assert.ThrowsAny<Exception>(() => features.CreateVectorStore(config));
    }

    // T-060
    [Fact]
    public void CreateFilterTranslator_ReturnsNonNull()
    {
        Assert.NotNull(VectorStoreDiscovery.GetProvider("sqlite")!.CreateFilterTranslator());
    }

    // T-061: SQLite returns SqliteFilterResult — Sql must not contain literal value
    [Fact]
    public void Translate_EqFilter()
    {
        var translator = VectorStoreDiscovery.GetProvider("sqlite")!.CreateFilterTranslator();
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
        var translator = VectorStoreDiscovery.GetProvider("sqlite")!.CreateFilterTranslator();
        var result = translator.Translate(TagFilter("userId", "u123"));
        Assert.NotNull(result);
        var sql = GetSql(result);
        // SQLite resolves to json_extract(tags, '$.userId')
        Assert.Contains("tags", sql);
    }

    // T-063
    [Fact]
    public void Translate_And()
    {
        var translator = VectorStoreDiscovery.GetProvider("sqlite")!.CreateFilterTranslator();
        var result = translator.Translate(AndFilter(EqFilter("a", "1"), EqFilter("b", "2")));
        Assert.NotNull(result);
        Assert.Contains("AND", GetSql(result), StringComparison.OrdinalIgnoreCase);
    }

    // T-064
    [Fact]
    public void Translate_Or()
    {
        var translator = VectorStoreDiscovery.GetProvider("sqlite")!.CreateFilterTranslator();
        var result = translator.Translate(OrFilter(EqFilter("a", "1"), EqFilter("b", "2")));
        Assert.NotNull(result);
        Assert.Contains("OR", GetSql(result), StringComparison.OrdinalIgnoreCase);
    }

    // T-065
    [Fact]
    public void Translate_Not()
    {
        var translator = VectorStoreDiscovery.GetProvider("sqlite")!.CreateFilterTranslator();
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
        var translator = VectorStoreDiscovery.GetProvider("sqlite")!.CreateFilterTranslator();
        var result = translator.Translate(FilterForOp(op));
        Assert.NotNull(result);
    }

    // T-067
    [Fact]
    public void Translate_Null_ReturnsNull()
    {
        var translator = VectorStoreDiscovery.GetProvider("sqlite")!.CreateFilterTranslator();
        Assert.Null(translator.Translate(null!));
    }

    private static string GetSql(object result)
    {
        var prop = result.GetType().GetProperty("Sql");
        return (string?)prop?.GetValue(result) ?? string.Empty;
    }
}
