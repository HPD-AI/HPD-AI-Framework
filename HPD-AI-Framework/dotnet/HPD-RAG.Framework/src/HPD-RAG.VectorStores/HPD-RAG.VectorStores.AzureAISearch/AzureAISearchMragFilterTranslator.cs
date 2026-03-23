using System.Text;
using System.Text.Json;
using HPD.RAG.Core.Filters;

namespace HPD.RAG.VectorStores.AzureAISearch;

/// <summary>
/// Translates MragFilterNode AST to an Azure AI Search OData $filter string.
/// The "tag:{key}" property prefix maps to "tags/key" in the OData field path syntax.
///
/// All string values are single-quote escaped. Numeric values are emitted as-is.
/// Boolean values are emitted as JSON booleans (true/false).
/// </summary>
internal sealed class AzureAISearchMragFilterTranslator : IMragFilterTranslator
{
    public object? Translate(MragFilterNode? node)
    {
        if (node is null) return null;
        return BuildOData(node);
    }

    private static string BuildOData(MragFilterNode node)
    {
        return node.Op switch
        {
            "and" => BuildLogical(node, "and"),
            "or" => BuildLogical(node, "or"),
            "not" => BuildNot(node),
            "eq" => BuildComparison(node, "eq"),
            "neq" => BuildComparison(node, "ne"),
            "gt" => BuildComparison(node, "gt"),
            "gte" => BuildComparison(node, "ge"),
            "lt" => BuildComparison(node, "lt"),
            "lte" => BuildComparison(node, "le"),
            "contains" => BuildSearchFunction(node, "search.ismatch"),
            "startswith" => BuildStartsWith(node),
            _ => throw new NotSupportedException($"Azure AI Search filter operator '{node.Op}' is not supported.")
        };
    }

    private static string BuildLogical(MragFilterNode node, string op)
    {
        var children = node.Children ?? [];
        if (children.Length == 0) return op == "and" ? "true" : "false";
        var parts = Array.ConvertAll(children, c => $"({BuildOData(c)})");
        return string.Join($" {op} ", parts);
    }

    private static string BuildNot(MragFilterNode node)
    {
        var child = node.Children?[0] ?? throw new InvalidOperationException("'not' requires exactly one child.");
        return $"not ({BuildOData(child)})";
    }

    private static string BuildComparison(MragFilterNode node, string op)
    {
        var field = ResolveField(node.Property!);
        var valueStr = FormatValue(node.Value);
        return $"{field} {op} {valueStr}";
    }

    private static string BuildSearchFunction(MragFilterNode node, string func)
    {
        var field = ResolveField(node.Property!);
        var value = node.Value?.GetString()
            ?? throw new InvalidOperationException("'contains' requires a string value.");
        var escaped = EscapeODataString(value);
        return $"search.ismatch('*{escaped}*', '{field}')";
    }

    private static string BuildStartsWith(MragFilterNode node)
    {
        var field = ResolveField(node.Property!);
        var value = node.Value?.GetString()
            ?? throw new InvalidOperationException("'startswith' requires a string value.");
        var escaped = EscapeODataString(value);
        return $"startswith({field}, '{escaped}')";
    }

    private static string ResolveField(string property)
    {
        if (property.StartsWith("tag:", StringComparison.Ordinal))
            return $"tags/{property.Substring(4)}";
        return property;
    }

    private static string FormatValue(JsonElement? element)
    {
        if (element is null) return "null";
        return element.Value.ValueKind switch
        {
            JsonValueKind.String => $"'{EscapeODataString(element.Value.GetString() ?? string.Empty)}'",
            JsonValueKind.Number when element.Value.TryGetInt32(out var i) => i.ToString(),
            JsonValueKind.Number => element.Value.GetDouble().ToString("G", System.Globalization.CultureInfo.InvariantCulture),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => "null"
        };
    }

    private static string EscapeODataString(string value)
        => value.Replace("'", "''");
}
