using System.Text.Json;

namespace HPD.Base;

/// <summary>Represents a relational index descriptor.</summary>
public sealed record RelationalIndexDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the store ID.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets or sets the parent object ref.</summary>
    public required string ParentObjectRef { get; init; }
    /// <summary>Gets or sets the native name.</summary>
    public required string NativeName { get; init; }
    /// <summary>Gets or sets the native path.</summary>
    public string? NativePath { get; init; }
    /// <summary>Gets or sets the unique.</summary>
    public bool Unique { get; init; }
    /// <summary>Gets or sets the primary.</summary>
    public bool Primary { get; init; }
    /// <summary>Gets or sets the method.</summary>
    public string? Method { get; init; }
    /// <summary>Gets or sets the parts.</summary>
    public required RelationalIndexPartDescriptor[] Parts { get; init; }
    /// <summary>Gets or sets the include column refs.</summary>
    public string[]? IncludeColumnRefs { get; init; }
    /// <summary>Gets or sets the predicate summary.</summary>
    public string? PredicateSummary { get; init; }
    /// <summary>Gets or sets the predicate redacted.</summary>
    public bool PredicateRedacted { get; init; } = true;
    /// <summary>Gets or sets the status summary.</summary>
    public string? StatusSummary { get; init; }
    /// <summary>Gets or sets the base index definition ref.</summary>
    public string? BaseIndexDefinitionRef { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; }
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational index part descriptor.</summary>
public sealed record RelationalIndexPartDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the ordinal.</summary>
    public int Ordinal { get; init; }
    /// <summary>Gets or sets the column ref.</summary>
    public string? ColumnRef { get; init; }
    /// <summary>Gets or sets the expression summary.</summary>
    public string? ExpressionSummary { get; init; }
    /// <summary>Gets or sets the expression redacted.</summary>
    public bool ExpressionRedacted { get; init; } = true;
    /// <summary>Gets or sets the sort direction.</summary>
    public string? SortDirection { get; init; }
    /// <summary>Gets or sets the null ordering.</summary>
    public string? NullOrdering { get; init; }
    /// <summary>Gets or sets the collation.</summary>
    public string? Collation { get; init; }
    /// <summary>Gets or sets the operator class.</summary>
    public string? OperatorClass { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; }
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}
