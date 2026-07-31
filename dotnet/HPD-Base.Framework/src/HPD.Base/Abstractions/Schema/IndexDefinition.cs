using System.Text.Json;

namespace HPD.Base;

public sealed record IndexDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string CollectionId { get; init; }
    public required IndexKind Kind { get; init; }
    public IndexPart[]? Parts { get; init; }
    public bool Unique { get; init; }
    public bool Primary { get; init; }
    public FilterExpression? Predicate { get; init; }
    public string? NativePredicate { get; init; }
    public IndexStatus Status { get; init; } = IndexStatus.Unknown;
    public EnforcementOwner Enforcement { get; init; } = EnforcementOwner.Store;
    public string? AccessMethod { get; init; }
    public string? NativeDefinition { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record IndexPart
{
    public required IndexPartKind Kind { get; init; }
    public string? FieldPath { get; init; }
    public string? Expression { get; init; }
    public IndexSortDirection Direction { get; init; } = IndexSortDirection.Asc;
    public IndexNullOrder Nulls { get; init; } = IndexNullOrder.Unspecified;
    public string? Collation { get; init; }
    public int? Length { get; init; }
    public string? OperatorClass { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}
