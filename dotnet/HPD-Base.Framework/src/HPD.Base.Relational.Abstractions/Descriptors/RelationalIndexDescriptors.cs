using System.Text.Json;
using HPD.Base;

namespace HPD.Base.Relational.Descriptors;

public sealed record RelationalIndexDescriptor
{
    public required string Id { get; init; }
    public required string StoreId { get; init; }
    public required string ParentObjectRef { get; init; }
    public required string NativeName { get; init; }
    public string? NativePath { get; init; }
    public bool Unique { get; init; }
    public bool Primary { get; init; }
    public string? Method { get; init; }
    public required RelationalIndexPartDescriptor[] Parts { get; init; }
    public string[]? IncludeColumnRefs { get; init; }
    public string? PredicateSummary { get; init; }
    public bool PredicateRedacted { get; init; } = true;
    public string? StatusSummary { get; init; }
    public string? BaseIndexDefinitionRef { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    public bool PublicSafe { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalIndexPartDescriptor
{
    public required string Id { get; init; }
    public int Ordinal { get; init; }
    public string? ColumnRef { get; init; }
    public string? ExpressionSummary { get; init; }
    public bool ExpressionRedacted { get; init; } = true;
    public string? SortDirection { get; init; }
    public string? NullOrdering { get; init; }
    public string? Collation { get; init; }
    public string? OperatorClass { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    public bool PublicSafe { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}
