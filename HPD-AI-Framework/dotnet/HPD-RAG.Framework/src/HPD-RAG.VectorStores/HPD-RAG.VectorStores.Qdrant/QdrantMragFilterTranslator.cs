using System.Text.Json;
using HPD.RAG.Core.Filters;

// Alias to avoid conflict with our own namespace segment "Qdrant"
using QdrantFilter = Qdrant.Client.Grpc.Filter;
using QdrantCondition = Qdrant.Client.Grpc.Condition;
using QdrantFieldCondition = Qdrant.Client.Grpc.FieldCondition;
using QdrantMatch = Qdrant.Client.Grpc.Match;
using QdrantRange = Qdrant.Client.Grpc.Range;

namespace HPD.RAG.VectorStores.Qdrant;

/// <summary>
/// Translates MragFilterNode AST to a Qdrant SDK Filter object.
/// The "tag:{key}" property prefix maps to payload field "tags.{key}".
/// </summary>
internal sealed class QdrantMragFilterTranslator : IMragFilterTranslator
{
    public object? Translate(MragFilterNode? node)
    {
        if (node is null) return null;
        return BuildFilter(node);
    }

    private static QdrantFilter BuildFilter(MragFilterNode node)
    {
        return node.Op switch
        {
            "and" => BuildAnd(node),
            "or" => BuildOr(node),
            "not" => BuildNot(node),
            "eq" => BuildMatch(node),
            "neq" => WrapNot(BuildMatch(node)),
            "gt" => BuildRange(node),
            "gte" => BuildRange(node),
            "lt" => BuildRange(node),
            "lte" => BuildRange(node),
            "contains" => BuildTextMatch(node),
            "startswith" => BuildTextMatch(node),
            _ => throw new NotSupportedException($"Qdrant filter operator '{node.Op}' is not supported.")
        };
    }

    private static QdrantFilter BuildAnd(MragFilterNode node)
    {
        var filter = new QdrantFilter();
        foreach (var child in node.Children ?? [])
            filter.Must.Add(new QdrantCondition { Filter = BuildFilter(child) });
        return filter;
    }

    private static QdrantFilter BuildOr(MragFilterNode node)
    {
        var filter = new QdrantFilter();
        foreach (var child in node.Children ?? [])
            filter.Should.Add(new QdrantCondition { Filter = BuildFilter(child) });
        return filter;
    }

    private static QdrantFilter BuildNot(MragFilterNode node)
    {
        var child = node.Children?[0] ?? throw new InvalidOperationException("'not' requires one child.");
        return WrapNot(BuildFilter(child));
    }

    private static QdrantFilter WrapNot(QdrantFilter inner)
    {
        var filter = new QdrantFilter();
        filter.MustNot.Add(new QdrantCondition { Filter = inner });
        return filter;
    }

    private static QdrantFilter BuildMatch(MragFilterNode node)
    {
        var fieldPath = ResolveField(node.Property!);
        var condition = new QdrantFieldCondition { Key = fieldPath };

        if (node.Value is null)
            throw new InvalidOperationException("'eq' filter node must have a Value.");

        condition.Match = node.Value.Value.ValueKind switch
        {
            JsonValueKind.String => new QdrantMatch { Keyword = node.Value.Value.GetString() },
            JsonValueKind.Number when node.Value.Value.TryGetInt64(out var l) => new QdrantMatch { Integer = l },
            JsonValueKind.True => new QdrantMatch { Boolean = true },
            JsonValueKind.False => new QdrantMatch { Boolean = false },
            _ => throw new InvalidOperationException($"Unsupported JsonElement kind for Qdrant match: {node.Value.Value.ValueKind}")
        };

        return new QdrantFilter { Must = { new QdrantCondition { Field = condition } } };
    }

    private static QdrantFilter BuildRange(MragFilterNode node)
    {
        var fieldPath = ResolveField(node.Property!);
        var value = node.Value?.GetDouble()
            ?? throw new InvalidOperationException($"Range operator '{node.Op}' requires a numeric value.");

        var numericRange = new QdrantRange();
        switch (node.Op)
        {
            case "gt": numericRange.Gt = value; break;
            case "gte": numericRange.Gte = value; break;
            case "lt": numericRange.Lt = value; break;
            case "lte": numericRange.Lte = value; break;
        }

        var condition = new QdrantFieldCondition { Key = fieldPath, Range = numericRange };
        return new QdrantFilter { Must = { new QdrantCondition { Field = condition } } };
    }

    private static QdrantFilter BuildTextMatch(MragFilterNode node)
    {
        var fieldPath = ResolveField(node.Property!);
        var value = node.Value?.GetString()
            ?? throw new InvalidOperationException($"Operator '{node.Op}' requires a string value.");

        // Qdrant text match — uses full-text search
        var condition = new QdrantFieldCondition
        {
            Key = fieldPath,
            Match = new QdrantMatch { Text = value }
        };
        return new QdrantFilter { Must = { new QdrantCondition { Field = condition } } };
    }

    private static string ResolveField(string property)
    {
        if (property.StartsWith("tag:", StringComparison.Ordinal))
            return $"tags.{property.Substring(4)}";
        return property;
    }
}
