using System.Text.Json;
using HPD.RAG.Core.Serialization;

namespace HPD.RAG.Core.Filters;

/// <summary>
/// Fluent factory for constructing MragFilterNode ASTs.
/// </summary>
public static class MragFilter
{
    public static MragFilterNode Eq(string property, string value)
        => Leaf("eq", property, Elem(value));

    public static MragFilterNode Eq(string property, int value)
        => Leaf("eq", property, Elem(value));

    public static MragFilterNode Eq(string property, double value)
        => Leaf("eq", property, Elem(value));

    public static MragFilterNode Eq(string property, bool value)
        => Leaf("eq", property, Elem(value));

    public static MragFilterNode Eq(string property, DateTimeOffset value)
        => Leaf("eq", property, ElemString(value.ToString("O")));

    public static MragFilterNode Neq(string property, string value)
        => Leaf("neq", property, Elem(value));

    public static MragFilterNode Gt(string property, double value)
        => Leaf("gt", property, Elem(value));

    public static MragFilterNode Gt(string property, DateTimeOffset value)
        => Leaf("gt", property, ElemString(value.ToString("O")));

    public static MragFilterNode Gte(string property, double value)
        => Leaf("gte", property, Elem(value));

    public static MragFilterNode Gte(string property, DateTimeOffset value)
        => Leaf("gte", property, ElemString(value.ToString("O")));

    public static MragFilterNode Lt(string property, double value)
        => Leaf("lt", property, Elem(value));

    public static MragFilterNode Lt(string property, DateTimeOffset value)
        => Leaf("lt", property, ElemString(value.ToString("O")));

    public static MragFilterNode Lte(string property, double value)
        => Leaf("lte", property, Elem(value));

    public static MragFilterNode Lte(string property, DateTimeOffset value)
        => Leaf("lte", property, ElemString(value.ToString("O")));

    public static MragFilterNode Contains(string property, string value)
        => Leaf("contains", property, Elem(value));

    public static MragFilterNode StartsWith(string property, string value)
        => Leaf("startswith", property, Elem(value));

    /// <summary>
    /// Tag filter — applies the "tag:{key}" property naming convention.
    /// Compiles to a filter against the indexed tags column on the vector store record.
    /// </summary>
    public static MragFilterNode Tag(string key, string value)
        => Leaf("eq", $"tag:{key}", Elem(value));

    public static MragFilterNode And(params MragFilterNode[] children)
        => new() { Op = "and", Children = children };

    public static MragFilterNode Or(params MragFilterNode[] children)
        => new() { Op = "or", Children = children };

    public static MragFilterNode Not(MragFilterNode child)
        => new() { Op = "not", Children = [child] };

    private static MragFilterNode Leaf(string op, string property, JsonElement value)
        => new() { Op = op, Property = property, Value = value };

    // AOT-safe JsonElement construction from source-generated context
    private static JsonElement Elem(string v)
        => JsonSerializer.SerializeToElement(v, MragJsonSerializerContext.Shared.String);

    private static JsonElement Elem(int v)
        => JsonSerializer.SerializeToElement(v, MragJsonSerializerContext.Shared.Int32);

    private static JsonElement Elem(double v)
        => JsonSerializer.SerializeToElement(v, MragJsonSerializerContext.Shared.Double);

    private static JsonElement Elem(bool v)
        => JsonSerializer.SerializeToElement(v, MragJsonSerializerContext.Shared.Boolean);

    private static JsonElement ElemString(string v)
        => JsonSerializer.SerializeToElement(v, MragJsonSerializerContext.Shared.String);
}
