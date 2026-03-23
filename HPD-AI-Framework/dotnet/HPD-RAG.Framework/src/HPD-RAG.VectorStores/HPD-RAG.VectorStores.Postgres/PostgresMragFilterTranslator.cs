using System.Text;
using System.Text.Json;
using HPD.RAG.Core.Filters;

namespace HPD.RAG.VectorStores.Postgres;

/// <summary>
/// Translates MragFilterNode AST to a PostgreSQL WHERE clause with $N-style positional parameters.
/// Returns a <see cref="PostgresFilterResult"/> containing the SQL fragment and parameter list.
/// The "tag:{key}" property prefix maps to a JSON containment check against the tags column.
/// </summary>
internal sealed class PostgresMragFilterTranslator : IMragFilterTranslator
{
    public object? Translate(MragFilterNode? node)
    {
        if (node is null) return null;

        var parameters = new List<object?>();
        var sql = BuildSql(node, parameters);
        return new PostgresFilterResult(sql, parameters);
    }

    private static string BuildSql(MragFilterNode node, List<object?> parameters)
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
            _ => throw new NotSupportedException($"Postgres filter operator '{node.Op}' is not supported.")
        };
    }

    private static string BuildLogical(MragFilterNode node, string op, List<object?> parameters)
    {
        var children = node.Children ?? [];
        if (children.Length == 0) return op == "AND" ? "TRUE" : "FALSE";
        var parts = Array.ConvertAll(children, c => BuildSql(c, parameters));
        return $"({string.Join($" {op} ", parts)})";
    }

    private static string BuildNot(MragFilterNode node, List<object?> parameters)
    {
        var child = node.Children?[0] ?? throw new InvalidOperationException("'not' operator requires exactly one child.");
        return $"(NOT {BuildSql(child, parameters)})";
    }

    private static string BuildComparison(MragFilterNode node, string op, List<object?> parameters)
    {
        var property = node.Property ?? throw new InvalidOperationException("Comparison filter node must have a Property.");
        var column = ResolveColumn(property, out var tagKey);

        parameters.Add(ExtractValue(node.Value));
        var paramIndex = parameters.Count;

        if (tagKey is not null)
        {
            // tags column is jsonb: tags->>'key' = $N
            parameters[^1] = tagKey; // first param is the tag key
            var keyIndex = parameters.Count;
            parameters.Add(ExtractValue(node.Value));
            var valIndex = parameters.Count;
            return $"(tags->>${keyIndex} {op} ${valIndex}::text)";
        }

        return $"({column} {op} ${paramIndex})";
    }

    private static string BuildLike(MragFilterNode node, string patternTemplate, List<object?> parameters)
    {
        var property = node.Property ?? throw new InvalidOperationException("String filter node must have a Property.");
        var column = ResolveColumn(property, out var tagKey);
        var rawValue = node.Value?.GetString()
            ?? throw new InvalidOperationException($"Operator '{node.Op}' requires a string value.");

        if (tagKey is not null)
        {
            parameters.Add(tagKey);
            var keyIndex = parameters.Count;
            parameters.Add(string.Format(patternTemplate, rawValue));
            var valIndex = parameters.Count;
            return $"(tags->>${keyIndex} ILIKE ${valIndex})";
        }

        parameters.Add(string.Format(patternTemplate, rawValue));
        return $"({column} ILIKE ${parameters.Count})";
    }

    private static string ResolveColumn(string property, out string? tagKey)
    {
        if (property.StartsWith("tag:", StringComparison.Ordinal))
        {
            tagKey = property.Substring(4);
            return "tags";
        }
        tagKey = null;
        return $"\"{property}\"";
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
/// Contains the SQL WHERE clause fragment and ordered parameter list produced by <see cref="PostgresMragFilterTranslator"/>.
/// </summary>
public sealed class PostgresFilterResult
{
    public string Sql { get; }
    public IReadOnlyList<object?> Parameters { get; }

    public PostgresFilterResult(string sql, List<object?> parameters)
    {
        Sql = sql;
        Parameters = parameters.AsReadOnly();
    }
}
