using System.Text.Json;
using HPD.RAG.Core.Filters;

namespace HPD.RAG.VectorStores.InMemory;

/// <summary>
/// Translates MragFilterNode AST to a Func&lt;Dictionary&lt;string,object?&gt;, bool&gt; predicate
/// suitable for in-memory filtering. Returns null when node is null (no filter).
///
/// Property names with the "tag:" prefix are resolved against a "tags" dictionary entry
/// in the record's metadata dictionary.
/// </summary>
internal sealed class InMemoryMragFilterTranslator : IMragFilterTranslator
{
    public object? Translate(MragFilterNode? node)
    {
        if (node is null) return null;

        Func<Dictionary<string, object?>, bool> predicate = BuildPredicate(node);
        return predicate;
    }

    private static Func<Dictionary<string, object?>, bool> BuildPredicate(MragFilterNode node)
    {
        return node.Op switch
        {
            "and" => BuildAnd(node),
            "or" => BuildOr(node),
            "not" => BuildNot(node),
            "eq" => BuildComparison(node, (a, b) => CompareValues(a, b) == 0),
            "neq" => BuildComparison(node, (a, b) => CompareValues(a, b) != 0),
            "gt" => BuildComparison(node, (a, b) => CompareValues(a, b) > 0),
            "gte" => BuildComparison(node, (a, b) => CompareValues(a, b) >= 0),
            "lt" => BuildComparison(node, (a, b) => CompareValues(a, b) < 0),
            "lte" => BuildComparison(node, (a, b) => CompareValues(a, b) <= 0),
            "contains" => BuildStringOp(node, (prop, val) => prop.Contains(val, StringComparison.OrdinalIgnoreCase)),
            "startswith" => BuildStringOp(node, (prop, val) => prop.StartsWith(val, StringComparison.OrdinalIgnoreCase)),
            _ => throw new NotSupportedException($"InMemory filter operator '{node.Op}' is not supported.")
        };
    }

    private static Func<Dictionary<string, object?>, bool> BuildAnd(MragFilterNode node)
    {
        var children = node.Children ?? [];
        var predicates = Array.ConvertAll(children, BuildPredicate);
        return record =>
        {
            foreach (var p in predicates)
                if (!p(record)) return false;
            return true;
        };
    }

    private static Func<Dictionary<string, object?>, bool> BuildOr(MragFilterNode node)
    {
        var children = node.Children ?? [];
        var predicates = Array.ConvertAll(children, BuildPredicate);
        return record =>
        {
            foreach (var p in predicates)
                if (p(record)) return true;
            return false;
        };
    }

    private static Func<Dictionary<string, object?>, bool> BuildNot(MragFilterNode node)
    {
        var child = node.Children?[0] ?? throw new InvalidOperationException("'not' operator requires exactly one child.");
        var inner = BuildPredicate(child);
        return record => !inner(record);
    }

    private static Func<Dictionary<string, object?>, bool> BuildComparison(
        MragFilterNode node, Func<object?, object?, bool> compare)
    {
        var property = node.Property ?? throw new InvalidOperationException("Comparison filter node must have a Property.");
        var filterValue = ExtractValue(node.Value);

        return record =>
        {
            var recordValue = ResolveProperty(record, property);
            return compare(recordValue, filterValue);
        };
    }

    private static Func<Dictionary<string, object?>, bool> BuildStringOp(
        MragFilterNode node, Func<string, string, bool> stringOp)
    {
        var property = node.Property ?? throw new InvalidOperationException("String filter node must have a Property.");
        var filterValue = node.Value?.GetString()
            ?? throw new InvalidOperationException($"String operator '{node.Op}' requires a string value.");

        return record =>
        {
            var recordValue = ResolveProperty(record, property);
            return recordValue is string s && stringOp(s, filterValue);
        };
    }

    /// <summary>
    /// Resolves a property from the record dictionary, handling "tag:{key}" prefix.
    /// </summary>
    private static object? ResolveProperty(Dictionary<string, object?> record, string property)
    {
        if (property.StartsWith("tag:", StringComparison.Ordinal))
        {
            var tagKey = property.Substring(4);
            if (record.TryGetValue("tags", out var tagsObj) && tagsObj is Dictionary<string, string> tags)
                return tags.TryGetValue(tagKey, out var tagVal) ? tagVal : null;
            return null;
        }

        record.TryGetValue(property, out var val);
        return val;
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
            JsonValueKind.Null => null,
            _ => element.Value.GetRawText()
        };
    }

    private static int CompareValues(object? a, object? b)
    {
        if (a is null && b is null) return 0;
        if (a is null) return -1;
        if (b is null) return 1;

        // Numeric comparison
        if (a is double da && b is double db) return da.CompareTo(db);
        if (a is int ia && b is int ib) return ia.CompareTo(ib);
        if (a is int ia2 && b is double db2) return ((double)ia2).CompareTo(db2);
        if (a is double da2 && b is int ib2) return da2.CompareTo((double)ib2);

        // String comparison
        if (a is string sa && b is string sb) return string.Compare(sa, sb, StringComparison.Ordinal);

        // Boolean comparison
        if (a is bool ba && b is bool bb) return ba.CompareTo(bb);

        return string.Compare(a.ToString(), b?.ToString(), StringComparison.Ordinal);
    }
}
