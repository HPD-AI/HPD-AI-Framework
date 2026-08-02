using System.Text.Json;

namespace HPD.Base;
/// <summary>Represents index Definition.</summary>
public sealed record IndexDefinition
{
    /// <summary>Gets or sets id.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets or sets collection Id.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets or sets kind.</summary>
    public required IndexKind Kind { get; init; }
    /// <summary>Gets or sets parts.</summary>
    public IndexPart[]? Parts { get; init; }
    /// <summary>Gets or sets unique.</summary>
    public bool Unique { get; init; }
    /// <summary>Gets or sets primary.</summary>
    public bool Primary { get; init; }
    /// <summary>Gets or sets predicate.</summary>
    public FilterExpression? Predicate { get; init; }
    /// <summary>Gets or sets native Predicate.</summary>
    public string? NativePredicate { get; init; }
    /// <summary>Gets or sets status.</summary>
    public IndexStatus Status { get; init; } = IndexStatus.Unknown;
    /// <summary>Gets or sets enforcement.</summary>
    public EnforcementOwner Enforcement { get; init; } = EnforcementOwner.Store;
    /// <summary>Gets or sets access Method.</summary>
    public string? AccessMethod { get; init; }
    /// <summary>Gets or sets native Definition.</summary>
    public string? NativeDefinition { get; init; }
    /// <summary>Gets or sets extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents index Part.</summary>
public sealed record IndexPart
{
    /// <summary>Gets or sets kind.</summary>
    public required IndexPartKind Kind { get; init; }
    /// <summary>Gets or sets field Id.</summary>
    public string? FieldId { get; init; }
    /// <summary>Gets or sets expression.</summary>
    public string? Expression { get; init; }
    /// <summary>Gets or sets direction.</summary>
    public IndexSortDirection Direction { get; init; } = IndexSortDirection.Asc;
    /// <summary>Gets or sets nulls.</summary>
    public IndexNullOrder Nulls { get; init; } = IndexNullOrder.Unspecified;
    /// <summary>Gets or sets collation.</summary>
    public string? Collation { get; init; }
    /// <summary>Gets or sets length.</summary>
    public int? Length { get; init; }
    /// <summary>Gets or sets operator Class.</summary>
    public string? OperatorClass { get; init; }
    /// <summary>Gets or sets extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}
