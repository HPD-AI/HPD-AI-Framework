namespace HPD.ML.Abstractions;

/// <summary>
/// Immutable description of named, typed, annotated columns with
/// explicit composition semantics and refinement levels.
/// </summary>
/// <remarks>
/// Schema inference from POCOs uses source generators (no reflection)
/// for Native AOT compatibility.
/// </remarks>
public interface ISchema
{
    IReadOnlyList<IColumn> Columns { get; }
    IColumn? FindByName(string name);
    IColumn? FindByQualifiedName(string qualifiedName);

    /// <summary>Column-concat merge with explicit conflict resolution.</summary>
    ISchema MergeHorizontal(ISchema other, ConflictPolicy policy);

    /// <summary>Row-append merge. Requires strict type compatibility.</summary>
    ISchema MergeVertical(ISchema other);

    /// <summary>True if this schema is a valid refinement of the approximate one.</summary>
    bool IsRefinementOf(ISchema approximate);

    RefinementLevel Level { get; }
}
