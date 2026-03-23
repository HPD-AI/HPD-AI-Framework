using System.Globalization;
using System.Text;
using System.Text.Json;
using HPD.RAG.Core.Filters;

namespace HPD.RAG.VectorStores.Milvus;

/// <summary>
/// Translates MragFilterNode AST to a Milvus boolean expression string.
/// Milvus uses a Python-like expression language for scalar filtering.
/// The "tag:{key}" property prefix maps to tags["key"] in Milvus JSON field syntax.
///
/// All string values are double-quote escaped inline (Milvus expr does not support parameters).
/// Numeric and boolean values are emitted as literals.
/// </summary>
internal sealed class MilvusMragFilterTranslator : IMragFilterTranslator
{
    public object? Translate(MragFilterNode? node)
    {
        if (node is null) return null;
        return BuildExpr(node);
    }

    private static string BuildExpr(MragFilterNode node)
    {
        return node.Op switch
        {
            "and" => BuildLogical(node, "&&"),
            "or" => BuildLogical(node, "||"),
            "not" => BuildNot(node),
            "eq" => BuildComparison(node, "=="),
            "neq" => BuildComparison(node, "!="),
            "gt" => BuildComparison(node, ">"),
            "gte" => BuildComparison(node, ">="),
            "lt" => BuildComparison(node, "<"),
            "lte" => BuildComparison(node, "<="),
            "contains" => BuildLike(node, "contains"),
            "startswith" => BuildLike(node, "startswith"),
            _ => throw new NotSupportedException($"Milvus filter operator '{node.Op}' is not supported.")
        };
    }

    private static string BuildLogical(MragFilterNode node, string op)
    {
        var children = node.Children ?? [];
        if (children.Length == 0) return op == "&&" ? "true" : "false";
        var parts = Array.ConvertAll(children, c => $"({BuildExpr(c)})");
        return string.Join($" {op} ", parts);
    }

    private static string BuildNot(MragFilterNode node)
    {
        var child = node.Children?[0] ?? throw new InvalidOperationException("'not' requires exactly one child.");
        return $"!({BuildExpr(child)})";
    }

    private static string BuildComparison(MragFilterNode node, string op)
    {
        var field = ResolveField(node.Property!);
        var value = FormatValue(node.Value);
        return $"{field} {op} {value}";
    }

    private static string BuildLike(MragFilterNode node, string mode)
    {
        var field = ResolveField(node.Property!);
        var value = node.Value?.GetString()
            ?? throw new InvalidOperationException($"Operator '{mode}' requires a string value.");

        var escaped = EscapeMilvusString(value);
        return mode switch
        {
            "startswith" => $"{field} like \"{escaped}%\"",
            "contains" => $"{field} like \"%{escaped}%\"",
            _ => $"{field} like \"{escaped}\""
        };
    }

    private static string ResolveField(string property)
    {
        if (property.StartsWith("tag:", StringComparison.Ordinal))
            return $"tags[\"{property.Substring(4)}\"]";
        return property;
    }

    private static string FormatValue(JsonElement? element)
    {
        if (element is null) return "null";
        return element.Value.ValueKind switch
        {
            JsonValueKind.String => $"\"{EscapeMilvusString(element.Value.GetString() ?? string.Empty)}\"",
            JsonValueKind.Number when element.Value.TryGetInt32(out var i) => i.ToString(CultureInfo.InvariantCulture),
            JsonValueKind.Number => element.Value.GetDouble().ToString("G", CultureInfo.InvariantCulture),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => "null"
        };
    }

    private static string EscapeMilvusString(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
