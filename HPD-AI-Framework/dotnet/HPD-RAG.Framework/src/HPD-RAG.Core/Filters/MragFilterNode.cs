using System.Text.Json;

namespace HPD.RAG.Core.Filters;

/// <summary>
/// Serializable filter AST node. Flows through HPD.Graph sockets and checkpoints safely.
/// At execution time each vector store provider compiles this to its native filter syntax
/// via IMragFilterTranslator — there is no central switch over provider keys.
///
/// Supported operators:
///   Comparison: "eq", "neq", "gt", "gte", "lt", "lte", "contains", "startswith"
///   Logical:    "and", "or", "not"
///
/// Property names beginning with "tag:" refer to the indexed tags column on the
/// vector store record (set via MragFilter.Tag(key, value)).
/// </summary>
public sealed record MragFilterNode
{
    /// <summary>Operator string — "eq", "neq", "gt", "gte", "lt", "lte", "contains", "startswith", "and", "or", "not".</summary>
    public required string Op { get; init; }

    /// <summary>Property name. Null for logical operators ("and", "or", "not"). "tag:{key}" for tag filters.</summary>
    public string? Property { get; init; }

    /// <summary>Filter value. Type-safe via JsonElement — survives checkpoint serialization without loss.</summary>
    public JsonElement? Value { get; init; }

    /// <summary>Child nodes for logical operators ("and", "or", "not").</summary>
    public MragFilterNode[]? Children { get; init; }
}
