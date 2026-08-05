using System.Text.Json;

namespace HPD.Base;

/// <summary>Represents a relational primary key descriptor.</summary>
public sealed record RelationalPrimaryKeyDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the store ID.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets or sets the table ref.</summary>
    public required string TableRef { get; init; }
    /// <summary>Gets or sets the column refs.</summary>
    public required string[] ColumnRefs { get; init; }
    /// <summary>Gets or sets the native name.</summary>
    public string? NativeName { get; init; }
    /// <summary>Gets or sets the record ID mapping kind.</summary>
    public RelationalRecordIdMappingKind RecordIdMappingKind { get; init; } = RelationalRecordIdMappingKind.NativePrimaryKey;
    /// <summary>Gets or sets the generated identity.</summary>
    public bool GeneratedIdentity { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; } = true;
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational foreign key descriptor.</summary>
public sealed record RelationalForeignKeyDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the store ID.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets or sets the source table ref.</summary>
    public required string SourceTableRef { get; init; }
    /// <summary>Gets or sets the column mappings.</summary>
    public required RelationalForeignKeyColumnMapping[] ColumnMappings { get; init; }
    /// <summary>Gets or sets the target table ref.</summary>
    public required string TargetTableRef { get; init; }
    /// <summary>Gets or sets the native name.</summary>
    public string? NativeName { get; init; }
    /// <summary>Gets or sets the source collection ID.</summary>
    public string? SourceCollectionId { get; init; }
    /// <summary>Gets or sets the target collection ID.</summary>
    public string? TargetCollectionId { get; init; }
    /// <summary>Gets or sets the base relation ref.</summary>
    public string? BaseRelationRef { get; init; }
    /// <summary>Gets or sets the cardinality summary.</summary>
    public string? CardinalitySummary { get; init; }
    /// <summary>Gets or sets the update action summary.</summary>
    public string? UpdateActionSummary { get; init; }
    /// <summary>Gets or sets the delete action summary.</summary>
    public string? DeleteActionSummary { get; init; }
    /// <summary>Gets or sets the deferrable.</summary>
    public bool Deferrable { get; init; }
    /// <summary>Gets or sets the initially deferred.</summary>
    public bool InitiallyDeferred { get; init; }
    /// <summary>Gets or sets the enforcement.</summary>
    public RelationalConstraintEnforcementKind Enforcement { get; init; } = RelationalConstraintEnforcementKind.Native;
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; } = true;
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational foreign key column mapping.</summary>
public sealed record RelationalForeignKeyColumnMapping
{
    /// <summary>Gets or sets the source column ref.</summary>
    public required string SourceColumnRef { get; init; }
    /// <summary>Gets or sets the target column ref.</summary>
    public required string TargetColumnRef { get; init; }
    /// <summary>Gets or sets the ordinal.</summary>
    public int Ordinal { get; init; }
}

/// <summary>Represents a relational unique constraint descriptor.</summary>
public sealed record RelationalUniqueConstraintDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the store ID.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets or sets the table ref.</summary>
    public required string TableRef { get; init; }
    /// <summary>Gets or sets the native name.</summary>
    public string? NativeName { get; init; }
    /// <summary>Gets or sets the column refs.</summary>
    public string[]? ColumnRefs { get; init; }
    /// <summary>Gets or sets the expression summaries.</summary>
    public string[]? ExpressionSummaries { get; init; }
    /// <summary>Gets or sets the predicate summary.</summary>
    public string? PredicateSummary { get; init; }
    /// <summary>Gets or sets the predicate redacted.</summary>
    public bool PredicateRedacted { get; init; } = true;
    /// <summary>Gets or sets the backing index ref.</summary>
    public string? BackingIndexRef { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; }
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational check constraint descriptor.</summary>
public sealed record RelationalCheckConstraintDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the store ID.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets or sets the table ref.</summary>
    public required string TableRef { get; init; }
    /// <summary>Gets or sets the native name.</summary>
    public string? NativeName { get; init; }
    /// <summary>Gets or sets the column refs.</summary>
    public string[]? ColumnRefs { get; init; }
    /// <summary>Gets or sets the normalized kind.</summary>
    public string? NormalizedKind { get; init; }
    /// <summary>Gets or sets the expression summary.</summary>
    public string? ExpressionSummary { get; init; }
    /// <summary>Gets or sets the expression redacted.</summary>
    public bool ExpressionRedacted { get; init; } = true;
    /// <summary>Gets or sets the base validation rule refs.</summary>
    public string[]? BaseValidationRuleRefs { get; init; }
    /// <summary>Gets or sets the enforcement.</summary>
    public RelationalConstraintEnforcementKind Enforcement { get; init; } = RelationalConstraintEnforcementKind.Native;
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; }
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational provider constraint descriptor.</summary>
public sealed record RelationalProviderConstraintDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the store ID.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets or sets the parent object ref.</summary>
    public required string ParentObjectRef { get; init; }
    /// <summary>Gets or sets the kind.</summary>
    public required string Kind { get; init; }
    /// <summary>Gets or sets the native name.</summary>
    public string? NativeName { get; init; }
    /// <summary>Gets or sets the summary.</summary>
    public string? Summary { get; init; }
    /// <summary>Gets or sets the summary redacted.</summary>
    public bool SummaryRedacted { get; init; } = true;
    /// <summary>Gets or sets the enforcement.</summary>
    public RelationalConstraintEnforcementKind Enforcement { get; init; } = RelationalConstraintEnforcementKind.Unknown;
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; }
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}
