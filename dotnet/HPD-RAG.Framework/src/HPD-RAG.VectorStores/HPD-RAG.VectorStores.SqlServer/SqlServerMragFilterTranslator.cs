using System.Globalization;
using System.Text.Json;
using HPD.RAG.Core.Filters;

namespace HPD.RAG.VectorStores.SqlServer;

/// <summary>
/// Translates MragFilterNode AST to a SQL Server WHERE clause string with @p0-style named parameters.
/// Returns a <see cref="SqlServerFilterResult"/> containing the SQL fragment and parameter list.
/// The "tag:{key}" property prefix maps to JSON_VALUE(tags, '$.key') in T-SQL.
/// </summary>
internal sealed class SqlServerMragFilterTranslator : IMragFilterTranslator
{
    public object? Translate(MragFilterNode? node)
    {
        if (node is null) return null;

        var parameters = new List<(string Name, object? Value)>();
        var sql = BuildSql(node, parameters);
        return new SqlServerFilterResult(sql, parameters);
    }

    private static string BuildSql(MragFilterNode node, List<(string, object?)> parameters)
    {
        return node.Op switch
        {
            "and" => BuildLogical(node, "AND", parameters),
            "or" => BuildLogical(node, "OR", parameters),
            "not" => BuildNot(node, parameters),
            "eq" => BuildComparison(node, "=", parameters),
            "neq" => BuildComparison(node, "<>", parameters),
            "gt" => BuildComparison(node, ">", parameters),
            "gte" => BuildComparison(node, ">=", parameters),
            "lt" => BuildComparison(node, "<", parameters),
            "lte" => BuildComparison(node, "<=", parameters),
            "contains" => BuildLike(node, "%{0}%", parameters),
            "startswith" => BuildLike(node, "{0}%", parameters),
            _ => throw new NotSupportedException($"SQL Server filter operator '{node.Op}' is not supported.")
        };
    }

    private static string BuildLogical(MragFilterNode node, string op, List<(string, object?)> parameters)
    {
        var children = node.Children ?? [];
        if (children.Length == 0) return op == "AND" ? "1=1" : "1=0";
        var parts = Array.ConvertAll(children, c => $"({BuildSql(c, parameters)})");
        return string.Join($" {op} ", parts);
    }

    private static string BuildNot(MragFilterNode node, List<(string, object?)> parameters)
    {
        var child = node.Children?[0] ?? throw new InvalidOperationException("'not' requires exactly one child.");
        return $"NOT ({BuildSql(child, parameters)})";
    }

    private static string BuildComparison(MragFilterNode node, string op, List<(string, object?)> parameters)
    {
        var (column, isTag, tagKey) = ResolveColumn(node.Property!);
        var paramName = $"@p{parameters.Count}";
        parameters.Add((paramName, ExtractValue(node.Value)));

        return isTag
            ? $"JSON_VALUE({column}, '$.{tagKey}') {op} {paramName}"
            : $"[{column}] {op} {paramName}";
    }

    private static string BuildLike(MragFilterNode node, string patternTemplate, List<(string, object?)> parameters)
    {
        var (column, isTag, tagKey) = ResolveColumn(node.Property!);
        var rawValue = node.Value?.GetString()
            ?? throw new InvalidOperationException($"Operator '{node.Op}' requires a string value.");

        var paramName = $"@p{parameters.Count}";
        parameters.Add((paramName, string.Format(patternTemplate, rawValue)));

        return isTag
            ? $"JSON_VALUE({column}, '$.{tagKey}') LIKE {paramName}"
            : $"[{column}] LIKE {paramName}";
    }

    private static (string Column, bool IsTag, string? TagKey) ResolveColumn(string property)
    {
        if (property.StartsWith("tag:", StringComparison.Ordinal))
            return ("tags", true, property.Substring(4));
        return (property, false, null);
    }

    private static object? ExtractValue(JsonElement? element)
    {
        if (element is null) return null;
        return element.Value.ValueKind switch
        {
            JsonValueKind.String => element.Value.GetString(),
            JsonValueKind.Number when element.Value.TryGetInt32(out var i) => (object)i,
            JsonValueKind.Number => element.Value.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }
}

/// <summary>
/// Contains the T-SQL WHERE clause fragment and named parameter list produced by <see cref="SqlServerMragFilterTranslator"/>.
/// </summary>
public sealed class SqlServerFilterResult
{
    public string Sql { get; }
    public IReadOnlyList<(string Name, object? Value)> Parameters { get; }

    public SqlServerFilterResult(string sql, List<(string Name, object? Value)> parameters)
    {
        Sql = sql;
        Parameters = parameters.AsReadOnly();
    }
}
