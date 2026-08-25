namespace HPD.Base;

/// <summary>Declares one ordered part of a generated exact logical index.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class BaseIndexPartAttribute(string indexId, int ordinal, string field) : Attribute
{
    /// <summary>Gets the owning logical index ID.</summary>
    public string IndexId { get; } = indexId;
    /// <summary>Gets the zero-based part ordinal.</summary>
    public int Ordinal { get; } = ordinal;
    /// <summary>Gets the CLR property name.</summary>
    public string Field { get; } = field;
    /// <summary>Gets or sets the exact direction.</summary>
    public BaseIndexSortDirection Direction { get; set; }
    /// <summary>Gets or sets the exact collation.</summary>
    public BaseIndexCollation Collation { get; set; }
    /// <summary>Gets or sets the exact missing/null order.</summary>
    public BaseIndexNullOrder NullOrder { get; set; }
}
