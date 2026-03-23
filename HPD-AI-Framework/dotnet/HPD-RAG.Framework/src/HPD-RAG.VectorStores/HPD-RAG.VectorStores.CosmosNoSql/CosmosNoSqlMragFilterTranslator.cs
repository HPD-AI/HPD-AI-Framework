using System.Globalization;
using System.Text;
using System.Text.Json;
using HPD.RAG.Core.Filters;

namespace HPD.RAG.VectorStores.CosmosNoSql;

/// <summary>
/// Translates MragFilterNode AST to a Cosmos DB NoSQL WHERE clause string.
/// Uses Cosmos SQL syntax with parameterized @pN values for security.
/// The "tag:{key}" property prefix maps to "c.tags['key']" in Cosmos SQL.
/// Returns a <see cref="CosmosNoSqlFilterResult"/> with the WHERE fragment and parameters.
/// </summary>
internal sealed class CosmosNoSqlMragFilterTranslator : IMragFilterTranslator
{
    public object? Translate(MragFilterNode? node)
    {
        if (node is null) return null;

        var parameters = new List<(string Name, object? Value)>();
        var sql = BuildSql(node, parameters);
        return new CosmosNoSqlFilterResult(sql, parameters);
    }

    private static string BuildSql(MragFilterNode node, List<(string, object?)> parameters)
    {
        return node.Op switch
        {
            "and" => BuildLogical(node, "AND", parameters),
            "or" => BuildLogical(node, "OR", parameters),
            "not" => BuildNot(node, parameters),
            "eq" => BuildComparison(node, "=", parameters),
            "neq" => BuildComparison(node, "!=", parameters),
            "gt" => BuildComparison(node, ">", parameters),
            "gte" => BuildComparison(node, ">=", parameters),
            "lt" => BuildComparison(node, "<", parameters),
            "lte" => BuildComparison(node, "<=", parameters),
            "contains" => BuildStringFunction(node, "CONTAINS", parameters),
            "startswith" => BuildStringFunction(node, "STARTSWITH", parameters),
            _ => throw new NotSupportedException($"Cosmos NoSQL filter operator '{node.Op}' is not supported.")
        };
    }

    private static string BuildLogical(MragFilterNode node, string op, List<(string, object?)> parameters)
    {
        var children = node.Children ?? [];
        if (children.Length == 0) return op == "AND" ? "true" : "false";
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
        var field = ResolveField(node.Property!);
        var paramName = $"@p{parameters.Count}";
        parameters.Add((paramName, ExtractValue(node.Value)));
        return $"{field} {op} {paramName}";
    }

    private static string BuildStringFunction(MragFilterNode node, string func, List<(string, object?)> parameters)
    {
        var field = ResolveField(node.Property!);
        var paramName = $"@p{parameters.Count}";
        var value = node.Value?.GetString()
            ?? throw new InvalidOperationException($"'{func}' requires a string value.");
        parameters.Add((paramName, value));
        return $"{func}({field}, {paramName})";
    }

    private static string ResolveField(string property)
    {
        if (property.StartsWith("tag:", StringComparison.Ordinal))
            return $"c.tags['{property.Substring(4)}']";
        return $"c.{property}";
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
/// Contains the Cosmos SQL WHERE clause fragment and ordered parameter list.
/// </summary>
public sealed class CosmosNoSqlFilterResult
{
    public string Sql { get; }
    public IReadOnlyList<(string Name, object? Value)> Parameters { get; }

    public CosmosNoSqlFilterResult(string sql, List<(string Name, object? Value)> parameters)
    {
        Sql = sql;
        Parameters = parameters.AsReadOnly();
    }
}
