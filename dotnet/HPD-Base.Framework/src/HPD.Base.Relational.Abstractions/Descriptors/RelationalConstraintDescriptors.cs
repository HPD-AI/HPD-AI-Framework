using System.Text.Json;
using HPD.Base;

namespace HPD.Base.Relational.Descriptors;

public sealed record RelationalPrimaryKeyDescriptor
{
    public required string Id { get; init; }
    public required string StoreId { get; init; }
    public required string TableRef { get; init; }
    public required string[] ColumnRefs { get; init; }
    public string? NativeName { get; init; }
    public RelationalRecordIdMappingKind RecordIdMappingKind { get; init; } = RelationalRecordIdMappingKind.NativePrimaryKey;
    public bool GeneratedIdentity { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    public bool PublicSafe { get; init; } = true;
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalForeignKeyDescriptor
{
    public required string Id { get; init; }
    public required string StoreId { get; init; }
    public required string SourceTableRef { get; init; }
    public required RelationalForeignKeyColumnMapping[] ColumnMappings { get; init; }
    public required string TargetTableRef { get; init; }
    public string? NativeName { get; init; }
    public string? SourceCollectionId { get; init; }
    public string? TargetCollectionId { get; init; }
    public string? BaseRelationRef { get; init; }
    public string? CardinalitySummary { get; init; }
    public string? UpdateActionSummary { get; init; }
    public string? DeleteActionSummary { get; init; }
    public bool Deferrable { get; init; }
    public bool InitiallyDeferred { get; init; }
    public RelationalConstraintEnforcementKind Enforcement { get; init; } = RelationalConstraintEnforcementKind.Native;
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    public bool PublicSafe { get; init; } = true;
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalForeignKeyColumnMapping
{
    public required string SourceColumnRef { get; init; }
    public required string TargetColumnRef { get; init; }
    public int Ordinal { get; init; }
}

public sealed record RelationalUniqueConstraintDescriptor
{
    public required string Id { get; init; }
    public required string StoreId { get; init; }
    public required string TableRef { get; init; }
    public string? NativeName { get; init; }
    public string[]? ColumnRefs { get; init; }
    public string[]? ExpressionSummaries { get; init; }
    public string? PredicateSummary { get; init; }
    public bool PredicateRedacted { get; init; } = true;
    public string? BackingIndexRef { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    public bool PublicSafe { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalCheckConstraintDescriptor
{
    public required string Id { get; init; }
    public required string StoreId { get; init; }
    public required string TableRef { get; init; }
    public string? NativeName { get; init; }
    public string[]? ColumnRefs { get; init; }
    public string? NormalizedKind { get; init; }
    public string? ExpressionSummary { get; init; }
    public bool ExpressionRedacted { get; init; } = true;
    public string[]? BaseValidationRuleRefs { get; init; }
    public RelationalConstraintEnforcementKind Enforcement { get; init; } = RelationalConstraintEnforcementKind.Native;
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    public bool PublicSafe { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalProviderConstraintDescriptor
{
    public required string Id { get; init; }
    public required string StoreId { get; init; }
    public required string ParentObjectRef { get; init; }
    public required string Kind { get; init; }
    public string? NativeName { get; init; }
    public string? Summary { get; init; }
    public bool SummaryRedacted { get; init; } = true;
    public RelationalConstraintEnforcementKind Enforcement { get; init; } = RelationalConstraintEnforcementKind.Unknown;
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    public bool PublicSafe { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}
