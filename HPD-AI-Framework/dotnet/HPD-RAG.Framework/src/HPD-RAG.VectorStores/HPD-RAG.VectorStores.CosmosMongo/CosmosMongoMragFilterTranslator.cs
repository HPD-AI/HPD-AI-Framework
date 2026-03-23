using System.Text.Json;
using HPD.RAG.Core.Filters;
using MongoDB.Bson;

namespace HPD.RAG.VectorStores.CosmosMongo;

/// <summary>
/// Translates MragFilterNode AST to a MongoDB BsonDocument filter compatible with
/// Azure Cosmos DB for MongoDB. Identical MQL semantics to the standard Mongo translator.
/// The "tag:{key}" property prefix maps to "tags.{key}" in the Cosmos document.
/// </summary>
internal sealed class CosmosMongoMragFilterTranslator : IMragFilterTranslator
{
    public object? Translate(MragFilterNode? node)
    {
        if (node is null) return null;
        return BuildBsonDocument(node);
    }

    private static BsonDocument BuildBsonDocument(MragFilterNode node)
    {
        return node.Op switch
        {
            "and" => BuildLogical(node, "$and"),
            "or" => BuildLogical(node, "$or"),
            "not" => BuildNot(node),
            "eq" => BuildComparison(node, "$eq"),
            "neq" => BuildComparison(node, "$ne"),
            "gt" => BuildComparison(node, "$gt"),
            "gte" => BuildComparison(node, "$gte"),
            "lt" => BuildComparison(node, "$lt"),
            "lte" => BuildComparison(node, "$lte"),
            "contains" => BuildRegex(node, "contains"),
            "startswith" => BuildRegex(node, "startswith"),
            _ => throw new NotSupportedException($"Cosmos MongoDB filter operator '{node.Op}' is not supported.")
        };
    }

    private static BsonDocument BuildLogical(MragFilterNode node, string op)
    {
        var children = node.Children ?? [];
        var array = new BsonArray(Array.ConvertAll(children, BuildBsonDocument));
        return new BsonDocument(op, array);
    }

    private static BsonDocument BuildNot(MragFilterNode node)
    {
        var child = node.Children?[0] ?? throw new InvalidOperationException("'not' requires exactly one child.");
        var inner = BuildBsonDocument(child);
        return new BsonDocument("$nor", new BsonArray { inner });
    }

    private static BsonDocument BuildComparison(MragFilterNode node, string op)
    {
        var field = ResolveField(node.Property!);
        var value = ToBsonValue(node.Value);
        return new BsonDocument(field, new BsonDocument(op, value));
    }

    private static BsonDocument BuildRegex(MragFilterNode node, string mode)
    {
        var field = ResolveField(node.Property!);
        var value = node.Value?.GetString()
            ?? throw new InvalidOperationException($"Operator '{mode}' requires a string value.");

        var pattern = mode == "startswith" ? $"^{EscapeRegex(value)}" : EscapeRegex(value);
        return new BsonDocument(field, new BsonDocument("$regex", new BsonRegularExpression(pattern, "i")));
    }

    private static string ResolveField(string property)
    {
        if (property.StartsWith("tag:", StringComparison.Ordinal))
            return $"tags.{property.Substring(4)}";
        return property;
    }

    private static BsonValue ToBsonValue(JsonElement? element)
    {
        if (element is null) return BsonNull.Value;
        return element.Value.ValueKind switch
        {
            JsonValueKind.String => new BsonString(element.Value.GetString()),
            JsonValueKind.Number when element.Value.TryGetInt32(out var i) => new BsonInt32(i),
            JsonValueKind.Number when element.Value.TryGetInt64(out var l) => new BsonInt64(l),
            JsonValueKind.Number => new BsonDouble(element.Value.GetDouble()),
            JsonValueKind.True => BsonBoolean.True,
            JsonValueKind.False => BsonBoolean.False,
            JsonValueKind.Null => BsonNull.Value,
            _ => BsonNull.Value
        };
    }

    private static string EscapeRegex(string value)
        => System.Text.RegularExpressions.Regex.Escape(value);
}
