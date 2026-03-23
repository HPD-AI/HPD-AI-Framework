using HPD.RAG.Core.Filters;
using HPD.RAG.Core.Providers.VectorStore;

namespace HPD.RAG.VectorStores.Tests.Shared;

/// <summary>
/// Abstract base providing helper assertions and filter node factories shared across all
/// 13 backend test classes. No database connections are made — tests exercise construction
/// and filter translation only.
/// </summary>
public abstract class VectorStoreTestBase
{
    // -------------------------------------------------------------------------
    // Filter node factories (thin wrappers for readability in test classes)
    // -------------------------------------------------------------------------

    protected static MragFilterNode EqFilter(string property, string value)
        => MragFilter.Eq(property, value);

    protected static MragFilterNode TagFilter(string key, string value)
        => MragFilter.Tag(key, value);

    protected static MragFilterNode AndFilter(MragFilterNode left, MragFilterNode right)
        => MragFilter.And(left, right);

    protected static MragFilterNode OrFilter(MragFilterNode left, MragFilterNode right)
        => MragFilter.Or(left, right);

    protected static MragFilterNode NotFilter(MragFilterNode child)
        => MragFilter.Not(child);

    // -------------------------------------------------------------------------
    // Comparison operator nodes for T-066 theories
    // -------------------------------------------------------------------------

    protected static MragFilterNode FilterForOp(string op) => op switch
    {
        "gt"         => MragFilter.Gt("score", 0.5),
        "gte"        => MragFilter.Gte("score", 0.5),
        "lt"         => MragFilter.Lt("score", 0.5),
        "lte"        => MragFilter.Lte("score", 0.5),
        "neq"        => MragFilter.Neq("category", "Draft"),
        "contains"   => MragFilter.Contains("title", "test"),
        "startswith" => MragFilter.StartsWith("title", "test"),
        _            => throw new ArgumentOutOfRangeException(nameof(op))
    };

    // -------------------------------------------------------------------------
    // Assertion helpers
    // -------------------------------------------------------------------------

    /// <summary>Assert a SQL-family result: the Sql fragment contains the expected keyword (case-insensitive).</summary>
    protected static void AssertSqlContains(object? result, string keyword)
    {
        Assert.NotNull(result);
        var sql = GetSqlFromResult(result);
        Assert.Contains(keyword, sql, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Assert a SQL-family result does NOT contain the literal string (case-sensitive).</summary>
    protected static void AssertSqlNotContainsLiteral(object? result, string literal)
    {
        Assert.NotNull(result);
        var sql = GetSqlFromResult(result);
        Assert.DoesNotContain(literal, sql);
    }

    private static string GetSqlFromResult(object result)
    {
        // All SQL translators return a result object with a Sql property.
        var sqlProp = result.GetType().GetProperty("Sql");
        if (sqlProp is not null)
            return (string?)sqlProp.GetValue(result) ?? string.Empty;

        // For translators that return the SQL string directly (e.g. Milvus, Redis, AzureAISearch).
        if (result is string s)
            return s;

        return result.ToString() ?? string.Empty;
    }
}
