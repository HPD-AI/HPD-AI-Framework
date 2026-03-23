using System.Text.Json;
using HPD.RAG.Core.Filters;

namespace HPD.RAG.VectorStores.Weaviate;

/// <summary>
/// Translates MragFilterNode AST to a Weaviate GraphQL where operand object.
/// Returns a <see cref="WeaviateWhereOperand"/> representing the filter tree.
/// The "tag:{key}" property prefix maps to "tags_{key}" in Weaviate's flat property schema.
/// </summary>
internal sealed class WeaviateMragFilterTranslator : IMragFilterTranslator
{
    public object? Translate(MragFilterNode? node)
    {
        if (node is null) return null;
        return BuildOperand(node);
    }

    private static WeaviateWhereOperand BuildOperand(MragFilterNode node)
    {
        return node.Op switch
        {
            "and" => BuildLogical(node, "And"),
            "or" => BuildLogical(node, "Or"),
            "not" => BuildNot(node),
            "eq" => BuildLeaf(node, "Equal"),
            "neq" => BuildLeaf(node, "NotEqual"),
            "gt" => BuildLeaf(node, "GreaterThan"),
            "gte" => BuildLeaf(node, "GreaterThanEqual"),
            "lt" => BuildLeaf(node, "LessThan"),
            "lte" => BuildLeaf(node, "LessThanEqual"),
            "contains" => BuildLeaf(node, "Like"),
            "startswith" => BuildLeafStartsWith(node),
            _ => throw new NotSupportedException($"Weaviate filter operator '{node.Op}' is not supported.")
        };
    }

    private static WeaviateWhereOperand BuildLogical(MragFilterNode node, string @operator)
    {
        var operands = Array.ConvertAll(node.Children ?? [], BuildOperand);
        return new WeaviateWhereOperand { Operator = @operator, Operands = operands };
    }

    private static WeaviateWhereOperand BuildNot(MragFilterNode node)
    {
        var child = node.Children?[0] ?? throw new InvalidOperationException("'not' operator requires exactly one child.");
        var inner = BuildOperand(child);
        return new WeaviateWhereOperand { Operator = "Not", Operands = [inner] };
    }

    private static WeaviateWhereOperand BuildLeaf(MragFilterNode node, string @operator)
    {
        var property = node.Property ?? throw new InvalidOperationException("Leaf filter node must have a Property.");
        var path = ResolveProperty(property);

        var operand = new WeaviateWhereOperand { Operator = @operator, Path = path };

        if (node.Value is not null)
        {
            switch (node.Value.Value.ValueKind)
            {
                case JsonValueKind.String:
                    var sv = node.Value.Value.GetString();
                    if (@operator == "Like")
                        operand.ValueText = $"*{sv}*";
                    else
                        operand.ValueText = sv;
                    break;
                case JsonValueKind.Number when node.Value.Value.TryGetInt32(out var i):
                    operand.ValueInt = i;
                    break;
                case JsonValueKind.Number:
                    operand.ValueNumber = node.Value.Value.GetDouble();
                    break;
                case JsonValueKind.True:
                    operand.ValueBoolean = true;
                    break;
                case JsonValueKind.False:
                    operand.ValueBoolean = false;
                    break;
            }
        }

        return operand;
    }

    private static WeaviateWhereOperand BuildLeafStartsWith(MragFilterNode node)
    {
        var property = node.Property ?? throw new InvalidOperationException("Leaf filter node must have a Property.");
        var path = ResolveProperty(property);
        var value = node.Value?.GetString()
            ?? throw new InvalidOperationException("'startswith' requires a string value.");

        return new WeaviateWhereOperand
        {
            Operator = "Like",
            Path = path,
            ValueText = $"{value}*"
        };
    }

    private static string[] ResolveProperty(string property)
    {
        if (property.StartsWith("tag:", StringComparison.Ordinal))
            return [$"tags_{property.Substring(4)}"];
        return [property];
    }
}

/// <summary>
/// Represents a Weaviate GraphQL where operand. Can be a leaf condition or a logical combinator.
/// </summary>
public sealed class WeaviateWhereOperand
{
    public string Operator { get; set; } = string.Empty;
    public string[]? Path { get; set; }
    public WeaviateWhereOperand[]? Operands { get; set; }
    public string? ValueText { get; set; }
    public int? ValueInt { get; set; }
    public double? ValueNumber { get; set; }
    public bool? ValueBoolean { get; set; }
}
