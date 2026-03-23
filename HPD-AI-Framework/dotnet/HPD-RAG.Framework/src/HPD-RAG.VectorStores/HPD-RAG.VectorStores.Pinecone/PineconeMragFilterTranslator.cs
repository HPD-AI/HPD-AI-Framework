using System.Text;
using System.Text.Json;
using HPD.RAG.Core.Filters;

namespace HPD.RAG.VectorStores.Pinecone;

/// <summary>
/// Translates MragFilterNode AST to a Pinecone metadata filter JSON string.
/// Pinecone uses MongoDB-style filter operators: $eq, $ne, $gt, $gte, $lt, $lte, $in.
/// "tag:{key}" property prefix maps to "tags.{key}" in the Pinecone metadata namespace.
/// </summary>
internal sealed class PineconeMragFilterTranslator : IMragFilterTranslator
{
    public object? Translate(MragFilterNode? node)
    {
        if (node is null) return null;
        return BuildJson(node);
    }

    private static string BuildJson(MragFilterNode node)
    {
        var sb = new StringBuilder();
        BuildNode(node, sb);
        return sb.ToString();
    }

    private static void BuildNode(MragFilterNode node, StringBuilder sb)
    {
        switch (node.Op)
        {
            case "and":
                sb.Append("{\"$and\":[");
                var andChildren = node.Children ?? [];
                for (int i = 0; i < andChildren.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    BuildNode(andChildren[i], sb);
                }
                sb.Append("]}");
                break;

            case "or":
                sb.Append("{\"$or\":[");
                var orChildren = node.Children ?? [];
                for (int i = 0; i < orChildren.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    BuildNode(orChildren[i], sb);
                }
                sb.Append("]}");
                break;

            case "not":
                var notChild = node.Children?[0] ?? throw new InvalidOperationException("'not' requires one child.");
                sb.Append("{\"$nor\":[");
                BuildNode(notChild, sb);
                sb.Append("]}");
                break;

            case "eq":
                AppendLeaf(sb, node.Property!, "$eq", node.Value);
                break;
            case "neq":
                AppendLeaf(sb, node.Property!, "$ne", node.Value);
                break;
            case "gt":
                AppendLeaf(sb, node.Property!, "$gt", node.Value);
                break;
            case "gte":
                AppendLeaf(sb, node.Property!, "$gte", node.Value);
                break;
            case "lt":
                AppendLeaf(sb, node.Property!, "$lt", node.Value);
                break;
            case "lte":
                AppendLeaf(sb, node.Property!, "$lte", node.Value);
                break;
            case "contains":
            case "startswith":
                // Pinecone metadata filters don't support regex/like; emit $eq as best approximation
                AppendLeaf(sb, node.Property!, "$eq", node.Value);
                break;

            default:
                throw new NotSupportedException($"Pinecone filter operator '{node.Op}' is not supported.");
        }
    }

    private static void AppendLeaf(StringBuilder sb, string property, string @operator, JsonElement? value)
    {
        var fieldName = ResolveProperty(property);
        sb.Append('{');
        sb.Append($"\"{EscapeJson(fieldName)}\":{{");
        sb.Append($"\"{@operator}\":");
        AppendValue(sb, value);
        sb.Append("}}");
    }

    private static void AppendValue(StringBuilder sb, JsonElement? element)
    {
        if (element is null) { sb.Append("null"); return; }
        switch (element.Value.ValueKind)
        {
            case JsonValueKind.String:
                sb.Append('"');
                sb.Append(EscapeJson(element.Value.GetString() ?? string.Empty));
                sb.Append('"');
                break;
            case JsonValueKind.Number when element.Value.TryGetInt32(out var i):
                sb.Append(i);
                break;
            case JsonValueKind.Number:
                sb.Append(element.Value.GetDouble());
                break;
            case JsonValueKind.True:
                sb.Append("true");
                break;
            case JsonValueKind.False:
                sb.Append("false");
                break;
            default:
                sb.Append("null");
                break;
        }
    }

    private static string ResolveProperty(string property)
    {
        if (property.StartsWith("tag:", StringComparison.Ordinal))
            return $"tags.{property.Substring(4)}";
        return property;
    }

    private static string EscapeJson(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
