using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Providers.VectorStore;
using HPD.RAG.VectorStores.Postgres;
using HPD.RAG.VectorStores.Tests.Shared;

namespace HPD.RAG.VectorStores.Tests.Backends;

public sealed class PostgresVectorStoreTests : VectorStoreTestBase
{
    static PostgresVectorStoreTests() => PostgresVectorStoreModule.Initialize();

    // -------------------------------------------------------------------------
    // T-055
    // -------------------------------------------------------------------------
    [Fact]
    public void ModuleInitializer_RegistersProvider()
    {
        var provider = VectorStoreDiscovery.GetProvider("postgres");
        Assert.NotNull(provider);
    }

    // -------------------------------------------------------------------------
    // T-056
    // -------------------------------------------------------------------------
    [Fact]
    public void Initialize_Idempotent()
    {
        PostgresVectorStoreModule.Initialize();
        PostgresVectorStoreModule.Initialize();

        var count = VectorStoreDiscovery.GetRegisteredProviders().Count(k => k == "postgres");
        Assert.Equal(1, count);
    }

    // -------------------------------------------------------------------------
    // T-057
    // -------------------------------------------------------------------------
    [Fact]
    public void ProviderKey_IsCorrectString()
    {
        var features = VectorStoreDiscovery.GetProvider("postgres");
        Assert.NotNull(features);
        Assert.Equal("postgres", features.ProviderKey);
    }

    // -------------------------------------------------------------------------
    // T-058: Postgres validates the connection string format at NpgsqlDataSourceBuilder.Build()
    // which occurs eagerly. Use a syntactically valid fake DSN.
    // -------------------------------------------------------------------------
    [Fact(Skip = "Postgres (Npgsql) validates the connection string at construction time and may attempt DNS resolution; no network is available in unit tests.")]
    public void CreateVectorStore_HappyPath() { }

    // -------------------------------------------------------------------------
    // T-059
    // -------------------------------------------------------------------------
    [Fact]
    public void CreateVectorStore_MissingConnectionString_Throws()
    {
        var features = VectorStoreDiscovery.GetProvider("postgres")!;
        var config = new VectorStoreConfig { ProviderKey = "postgres" };

        var ex = Assert.ThrowsAny<Exception>(() => features.CreateVectorStore(config));
        Assert.NotNull(ex);
    }

    // -------------------------------------------------------------------------
    // T-060
    // -------------------------------------------------------------------------
    [Fact]
    public void CreateFilterTranslator_ReturnsNonNull()
    {
        var features = VectorStoreDiscovery.GetProvider("postgres")!;
        var translator = features.CreateFilterTranslator();
        Assert.NotNull(translator);
    }

    // -------------------------------------------------------------------------
    // T-061: Postgres returns PostgresFilterResult; SQL must not contain the literal value.
    // -------------------------------------------------------------------------
    [Fact]
    public void Translate_EqFilter()
    {
        var translator = VectorStoreDiscovery.GetProvider("postgres")!.CreateFilterTranslator();
        var result = translator.Translate(EqFilter("category", "Technical"));

        Assert.NotNull(result);
        var sql = GetSql(result);
        // Parameterized — literal value must not appear in the SQL fragment
        Assert.DoesNotContain("Technical", sql);
        // Must contain a positional parameter placeholder
        Assert.Contains("$", sql);
    }

    // -------------------------------------------------------------------------
    // T-062
    // -------------------------------------------------------------------------
    [Fact]
    public void Translate_TagPrefix()
    {
        var translator = VectorStoreDiscovery.GetProvider("postgres")!.CreateFilterTranslator();
        var result = translator.Translate(TagFilter("userId", "u123"));

        Assert.NotNull(result);
        var sql = GetSql(result);
        Assert.Contains("tags", sql);
    }

    // -------------------------------------------------------------------------
    // T-063
    // -------------------------------------------------------------------------
    [Fact]
    public void Translate_And()
    {
        var translator = VectorStoreDiscovery.GetProvider("postgres")!.CreateFilterTranslator();
        var result = translator.Translate(AndFilter(EqFilter("a", "1"), EqFilter("b", "2")));

        Assert.NotNull(result);
        Assert.Contains("AND", GetSql(result), StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // T-064
    // -------------------------------------------------------------------------
    [Fact]
    public void Translate_Or()
    {
        var translator = VectorStoreDiscovery.GetProvider("postgres")!.CreateFilterTranslator();
        var result = translator.Translate(OrFilter(EqFilter("a", "1"), EqFilter("b", "2")));

        Assert.NotNull(result);
        Assert.Contains("OR", GetSql(result), StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // T-065
    // -------------------------------------------------------------------------
    [Fact]
    public void Translate_Not()
    {
        var translator = VectorStoreDiscovery.GetProvider("postgres")!.CreateFilterTranslator();
        var result = translator.Translate(NotFilter(EqFilter("a", "b")));

        Assert.NotNull(result);
        Assert.Contains("NOT", GetSql(result), StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // T-066
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
        var translator = VectorStoreDiscovery.GetProvider("postgres")!.CreateFilterTranslator();
        var result = translator.Translate(FilterForOp(op));
        Assert.NotNull(result);
    }

    // -------------------------------------------------------------------------
    // T-067
    // -------------------------------------------------------------------------
    [Fact]
    public void Translate_Null_ReturnsNull()
    {
        var translator = VectorStoreDiscovery.GetProvider("postgres")!.CreateFilterTranslator();
        var result = translator.Translate(null!);
        Assert.Null(result);
    }

    // Helper: extract Sql string from PostgresFilterResult
    private static string GetSql(object result)
    {
        var prop = result.GetType().GetProperty("Sql");
        return (string?)prop?.GetValue(result) ?? string.Empty;
    }
}
