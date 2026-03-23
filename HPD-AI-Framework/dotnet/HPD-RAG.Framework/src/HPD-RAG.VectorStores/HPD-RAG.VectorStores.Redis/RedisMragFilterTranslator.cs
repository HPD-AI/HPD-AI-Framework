using System.Text;
using System.Text.Json;
using HPD.RAG.Core.Filters;

namespace HPD.RAG.VectorStores.Redis;

/// <summary>
/// Translates MragFilterNode AST to a RediSearch query syntax string.
/// Returns a string compatible with FT.SEARCH filter expressions.
/// The "tag:{key}" property prefix maps to the @tags_{key} TAG field.
///
/// Numeric fields use @field:[min max] range notation.
/// Text/tag fields use @field:{value} or @field:value notation.
/// </summary>
internal sealed class RedisMragFilterTranslator : IMragFilterTranslator
{
    public object? Translate(MragFilterNode? node)
    {
        if (node is null) return null;
        return BuildQuery(node);
    }

    private static string BuildQuery(MragFilterNode node)
    {
        return node.Op switch
        {
            "and" => BuildLogical(node, " "),
            "or" => BuildLogical(node, " | "),
            "not" => BuildNot(node),
            "eq" => BuildEq(node),
            "neq" => BuildNeq(node),
            "gt" => BuildNumericRange(node, "gt"),
            "gte" => BuildNumericRange(node, "gte"),
            "lt" => BuildNumericRange(node, "lt"),
            "lte" => BuildNumericRange(node, "lte"),
            "contains" => BuildTextSearch(node, "contains"),
            "startswith" => BuildTextSearch(node, "startswith"),
            _ => throw new NotSupportedException($"Redis filter operator '{node.Op}' is not supported.")
        };
    }

    private static string BuildLogical(MragFilterNode node, string separator)
    {
        var children = node.Children ?? [];
        if (children.Length == 0) return "*";
        var parts = Array.ConvertAll(children, c => $"({BuildQuery(c)})");
        return string.Join(separator, parts);
    }

    private static string BuildNot(MragFilterNode node)
    {
        var child = node.Children?[0] ?? throw new InvalidOperationException("'not' requires exactly one child.");
        return $"-({BuildQuery(child)})";
    }

    private static string BuildEq(MragFilterNode node)
    {
        var field = ResolveField(node.Property!);
        if (node.Value is null) return "*";

        return node.Value.Value.ValueKind switch
        {
            JsonValueKind.String => $"@{field}:{{{EscapeTag(node.Value.Value.GetString() ?? string.Empty)}}}",
            JsonValueKind.Number when node.Value.Value.TryGetInt32(out var i) => $"@{field}:[{i} {i}]",
            JsonValueKind.Number => $"@{field}:[{node.Value.Value.GetDouble()} {node.Value.Value.GetDouble()}]",
            JsonValueKind.True => $"@{field}:{{true}}",
            JsonValueKind.False => $"@{field}:{{false}}",
            _ => "*"
        };
    }

    private static string BuildNeq(MragFilterNode node)
    {
        return $"-({BuildEq(node)})";
    }

    private static string BuildNumericRange(MragFilterNode node, string op)
    {
        var field = ResolveField(node.Property!);
        var value = node.Value?.GetDouble()
            ?? throw new InvalidOperationException($"Range operator '{op}' requires a numeric value.");

        return op switch
        {
            "gt" => $"@{field}:[({value} +inf]",
            "gte" => $"@{field}:[{value} +inf]",
            "lt" => $"@{field}:[-inf ({value}]",
            "lte" => $"@{field}:[-inf {value}]",
            _ => throw new InvalidOperationException($"Unknown range op: {op}")
        };
    }

    private static string BuildTextSearch(MragFilterNode node, string op)
    {
        var field = ResolveField(node.Property!);
        var value = node.Value?.GetString()
            ?? throw new InvalidOperationException($"Operator '{op}' requires a string value.");

        // RediSearch full text: use prefix search for startswith, wildcard for contains
        return op switch
        {
            "startswith" => $"@{field}:{EscapeTag(value)}*",
            "contains" => $"@{field}:*{EscapeTag(value)}*",
            _ => $"@{field}:{EscapeTag(value)}"
        };
    }

    private static string ResolveField(string property)
    {
        if (property.StartsWith("tag:", StringComparison.Ordinal))
            return $"tags_{property.Substring(4)}";
        return property;
    }

    private static string EscapeTag(string value)
        => value.Replace("-", "\\-").Replace(".", "\\.").Replace("@", "\\@")
                .Replace("!", "\\!").Replace("{", "\\{").Replace("}", "\\}");
}
