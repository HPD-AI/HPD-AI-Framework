using System.Text.Json;

namespace HPD.Base;

public sealed record RelationalTableDescriptor
{
    public required string Id { get; init; }
    public required string StoreId { get; init; }
    public required string NativeName { get; init; }
    public string? NativePath { get; init; }
    public string? DatabaseRef { get; init; }
    public string? CatalogRef { get; init; }
    public string? SchemaRef { get; init; }
    public RelationalTableKind Kind { get; init; } = RelationalTableKind.Table;
    public string[]? MappedCollectionIds { get; init; }
    public string? PrimaryKeyRef { get; init; }
    public string[]? ColumnRefs { get; init; }
    public string[]? ConstraintRefs { get; init; }
    public string[]? IndexRefs { get; init; }
    public bool ReadSupported { get; init; } = true;
    public bool WriteSupported { get; init; }
    public RelationalRecordIdMappingKind RowIdentityStrategy { get; init; } = RelationalRecordIdMappingKind.KeylessUnavailable;
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    public bool PublicSafe { get; init; } = true;
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalViewDescriptor
{
    public required string Id { get; init; }
    public required string StoreId { get; init; }
    public required string NativeName { get; init; }
    public string? NativePath { get; init; }
    public string? DatabaseRef { get; init; }
    public string? CatalogRef { get; init; }
    public string? SchemaRef { get; init; }
    public RelationalViewKind Kind { get; init; } = RelationalViewKind.Normal;
    public bool Materialized { get; init; }
    public bool? Updatable { get; init; }
    public bool? Insertable { get; init; }
    public RelationalViewMaterializationDescriptor? Materialization { get; init; }
    public string[]? MappedCollectionIds { get; init; }
    public string[]? ColumnRefs { get; init; }
    public string[]? SafeDependencyRefs { get; init; }
    public string? NativeDefinitionSummary { get; init; }
    public bool NativeDefinitionRedacted { get; init; } = true;
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    public bool PublicSafe { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalViewMaterializationDescriptor
{
    public required string Id { get; init; }
    public required string StoreId { get; init; }
    public RelationalViewMaterializationKind Kind { get; init; } = RelationalViewMaterializationKind.Unknown;
    public bool RefreshSupported { get; init; }
    public string? RefreshStatusSummary { get; init; }
    public DateTimeOffset? LastRefreshedAt { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    public bool PublicSafe { get; init; }
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}
