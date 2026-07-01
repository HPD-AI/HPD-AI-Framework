using System.Text.Json;
using HPD.Base;

namespace HPD.Base.Relational.Descriptors;

public sealed record RelationalCollectionMappingDescriptor
{
    public required string Id { get; init; }
    public required string StoreId { get; init; }
    public required string CollectionId { get; init; }
    public string? TableRef { get; init; }
    public string? ViewRef { get; init; }
    public RelationalMappingKind MappingKind { get; init; } = RelationalMappingKind.Table;
    public RelationalRecordIdMappingKind RecordIdMappingKind { get; init; } = RelationalRecordIdMappingKind.KeylessUnavailable;
    public string[]? RecordIdColumnRefs { get; init; }
    public string? RecordIdSummary { get; init; }
    public RelationalPayloadMappingKind PayloadMappingKind { get; init; } = RelationalPayloadMappingKind.Columns;
    public string? PayloadJsonColumnRef { get; init; }
    public string? RevisionColumnRef { get; init; }
    public string? CreatedAtColumnRef { get; init; }
    public string? UpdatedAtColumnRef { get; init; }
    public bool ListSupported { get; init; } = true;
    public bool GetSupported { get; init; } = true;
    public bool CreateSupported { get; init; }
    public bool PatchSupported { get; init; }
    public bool ReplaceSupported { get; init; }
    public bool DeleteSupported { get; init; }
    public string[]? FieldMappingRefs { get; init; }
    public string[]? RelationMappingRefs { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    public bool PublicSafe { get; init; } = true;
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalFieldMappingDescriptor
{
    public required string Id { get; init; }
    public required string StoreId { get; init; }
    public required string CollectionId { get; init; }
    public required string FieldPath { get; init; }
    public string? ColumnRef { get; init; }
    public string? JsonColumnRef { get; init; }
    public string? JsonPath { get; init; }
    public RelationalColumnTypeDescriptor? NativeType { get; init; }
    public RelationalColumnWriteBehavior WriteBehavior { get; init; } = RelationalColumnWriteBehavior.Writable;
    public RelationalFieldConversionKind ConversionKind { get; init; } = RelationalFieldConversionKind.None;
    public string? NullMissingSemanticsSummary { get; init; }
    public string? GeneratedColumnRef { get; init; }
    public string? DefaultColumnRef { get; init; }
    public string[]? ConstraintRefs { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    public bool PublicSafe { get; init; } = true;
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalRelationMappingDescriptor
{
    public required string Id { get; init; }
    public required string StoreId { get; init; }
    public required string SourceCollectionId { get; init; }
    public required string TargetCollectionId { get; init; }
    public string? BaseRelationRef { get; init; }
    public string? ForeignKeyRef { get; init; }
    public string[]? SourceFieldPaths { get; init; }
    public string[]? TargetFieldPaths { get; init; }
    public string? CardinalitySummary { get; init; }
    public bool IncludeExecutionSupported { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    public bool PublicSafe { get; init; } = true;
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}
