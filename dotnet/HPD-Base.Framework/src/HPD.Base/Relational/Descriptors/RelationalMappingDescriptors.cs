using System.Text.Json;

namespace HPD.Base;

/// <summary>Represents a relational collection mapping descriptor.</summary>
public sealed record RelationalCollectionMappingDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the store ID.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets or sets the collection ID.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets or sets the table ref.</summary>
    public string? TableRef { get; init; }
    /// <summary>Gets or sets the view ref.</summary>
    public string? ViewRef { get; init; }
    /// <summary>Gets or sets the mapping kind.</summary>
    public RelationalMappingKind MappingKind { get; init; } = RelationalMappingKind.Table;
    /// <summary>Gets or sets the record ID mapping kind.</summary>
    public RelationalRecordIdMappingKind RecordIdMappingKind { get; init; } = RelationalRecordIdMappingKind.KeylessUnavailable;
    /// <summary>Gets or sets the record ID column refs.</summary>
    public string[]? RecordIdColumnRefs { get; init; }
    /// <summary>Gets or sets the record ID summary.</summary>
    public string? RecordIdSummary { get; init; }
    /// <summary>Gets or sets the payload mapping kind.</summary>
    public RelationalPayloadMappingKind PayloadMappingKind { get; init; } = RelationalPayloadMappingKind.Columns;
    /// <summary>Gets or sets the payload JSON column ref.</summary>
    public string? PayloadJsonColumnRef { get; init; }
    /// <summary>Gets or sets the revision column ref.</summary>
    public string? RevisionColumnRef { get; init; }
    /// <summary>Gets or sets the created at column ref.</summary>
    public string? CreatedAtColumnRef { get; init; }
    /// <summary>Gets or sets the updated at column ref.</summary>
    public string? UpdatedAtColumnRef { get; init; }
    /// <summary>Gets or sets the list supported.</summary>
    public bool ListSupported { get; init; } = true;
    /// <summary>Gets or sets the get supported.</summary>
    public bool GetSupported { get; init; } = true;
    /// <summary>Gets or sets the create supported.</summary>
    public bool CreateSupported { get; init; }
    /// <summary>Gets or sets the patch supported.</summary>
    public bool PatchSupported { get; init; }
    /// <summary>Gets or sets the replace supported.</summary>
    public bool ReplaceSupported { get; init; }
    /// <summary>Gets or sets the delete supported.</summary>
    public bool DeleteSupported { get; init; }
    /// <summary>Gets or sets the field mapping refs.</summary>
    public string[]? FieldMappingRefs { get; init; }
    /// <summary>Gets or sets the relation mapping refs.</summary>
    public string[]? RelationMappingRefs { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; } = true;
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational field mapping descriptor.</summary>
public sealed record RelationalFieldMappingDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the store ID.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets or sets the collection ID.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets or sets the field path.</summary>
    public required string FieldPath { get; init; }
    /// <summary>Gets or sets the column ref.</summary>
    public string? ColumnRef { get; init; }
    /// <summary>Gets or sets the JSON column ref.</summary>
    public string? JsonColumnRef { get; init; }
    /// <summary>Gets or sets the JSON path.</summary>
    public string? JsonPath { get; init; }
    /// <summary>Gets or sets the native type.</summary>
    public RelationalColumnTypeDescriptor? NativeType { get; init; }
    /// <summary>Gets or sets the write behavior.</summary>
    public RelationalColumnWriteBehavior WriteBehavior { get; init; } = RelationalColumnWriteBehavior.Writable;
    /// <summary>Gets or sets the conversion kind.</summary>
    public RelationalFieldConversionKind ConversionKind { get; init; } = RelationalFieldConversionKind.None;
    /// <summary>Gets or sets the null missing semantics summary.</summary>
    public string? NullMissingSemanticsSummary { get; init; }
    /// <summary>Gets or sets the generated column ref.</summary>
    public string? GeneratedColumnRef { get; init; }
    /// <summary>Gets or sets the default column ref.</summary>
    public string? DefaultColumnRef { get; init; }
    /// <summary>Gets or sets the constraint refs.</summary>
    public string[]? ConstraintRefs { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; } = true;
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational relation mapping descriptor.</summary>
public sealed record RelationalRelationMappingDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the store ID.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets or sets the source collection ID.</summary>
    public required string SourceCollectionId { get; init; }
    /// <summary>Gets or sets the target collection ID.</summary>
    public required string TargetCollectionId { get; init; }
    /// <summary>Gets or sets the base relation ref.</summary>
    public string? BaseRelationRef { get; init; }
    /// <summary>Gets or sets the foreign key ref.</summary>
    public string? ForeignKeyRef { get; init; }
    /// <summary>Gets or sets the source field paths.</summary>
    public string[]? SourceFieldPaths { get; init; }
    /// <summary>Gets or sets the target field paths.</summary>
    public string[]? TargetFieldPaths { get; init; }
    /// <summary>Gets or sets the cardinality summary.</summary>
    public string? CardinalitySummary { get; init; }
    /// <summary>Gets or sets the include execution supported.</summary>
    public bool IncludeExecutionSupported { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; } = true;
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}
