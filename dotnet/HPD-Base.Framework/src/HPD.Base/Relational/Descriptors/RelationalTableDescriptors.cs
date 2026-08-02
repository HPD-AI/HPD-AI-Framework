using System.Text.Json;

namespace HPD.Base;

/// <summary>Represents a relational table descriptor.</summary>
public sealed record RelationalTableDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the store ID.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets or sets the native name.</summary>
    public required string NativeName { get; init; }
    /// <summary>Gets or sets the native path.</summary>
    public string? NativePath { get; init; }
    /// <summary>Gets or sets the database ref.</summary>
    public string? DatabaseRef { get; init; }
    /// <summary>Gets or sets the catalog ref.</summary>
    public string? CatalogRef { get; init; }
    /// <summary>Gets or sets the schema ref.</summary>
    public string? SchemaRef { get; init; }
    /// <summary>Gets or sets the kind.</summary>
    public RelationalTableKind Kind { get; init; } = RelationalTableKind.Table;
    /// <summary>Gets or sets the mapped collection IDs.</summary>
    public string[]? MappedCollectionIds { get; init; }
    /// <summary>Gets or sets the primary key ref.</summary>
    public string? PrimaryKeyRef { get; init; }
    /// <summary>Gets or sets the column refs.</summary>
    public string[]? ColumnRefs { get; init; }
    /// <summary>Gets or sets the constraint refs.</summary>
    public string[]? ConstraintRefs { get; init; }
    /// <summary>Gets or sets the index refs.</summary>
    public string[]? IndexRefs { get; init; }
    /// <summary>Gets or sets the read supported.</summary>
    public bool ReadSupported { get; init; } = true;
    /// <summary>Gets or sets the write supported.</summary>
    public bool WriteSupported { get; init; }
    /// <summary>Gets or sets the row identity strategy.</summary>
    public RelationalRecordIdMappingKind RowIdentityStrategy { get; init; } = RelationalRecordIdMappingKind.KeylessUnavailable;
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; } = true;
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational view descriptor.</summary>
public sealed record RelationalViewDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the store ID.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets or sets the native name.</summary>
    public required string NativeName { get; init; }
    /// <summary>Gets or sets the native path.</summary>
    public string? NativePath { get; init; }
    /// <summary>Gets or sets the database ref.</summary>
    public string? DatabaseRef { get; init; }
    /// <summary>Gets or sets the catalog ref.</summary>
    public string? CatalogRef { get; init; }
    /// <summary>Gets or sets the schema ref.</summary>
    public string? SchemaRef { get; init; }
    /// <summary>Gets or sets the kind.</summary>
    public RelationalViewKind Kind { get; init; } = RelationalViewKind.Normal;
    /// <summary>Gets or sets the materialized.</summary>
    public bool Materialized { get; init; }
    /// <summary>Gets or sets the updatable.</summary>
    public bool? Updatable { get; init; }
    /// <summary>Gets or sets the insertable.</summary>
    public bool? Insertable { get; init; }
    /// <summary>Gets or sets the materialization.</summary>
    public RelationalViewMaterializationDescriptor? Materialization { get; init; }
    /// <summary>Gets or sets the mapped collection IDs.</summary>
    public string[]? MappedCollectionIds { get; init; }
    /// <summary>Gets or sets the column refs.</summary>
    public string[]? ColumnRefs { get; init; }
    /// <summary>Gets or sets the safe dependency refs.</summary>
    public string[]? SafeDependencyRefs { get; init; }
    /// <summary>Gets or sets the native definition summary.</summary>
    public string? NativeDefinitionSummary { get; init; }
    /// <summary>Gets or sets the native definition redacted.</summary>
    public bool NativeDefinitionRedacted { get; init; } = true;
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; }
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational view materialization descriptor.</summary>
public sealed record RelationalViewMaterializationDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the store ID.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets or sets the kind.</summary>
    public RelationalViewMaterializationKind Kind { get; init; } = RelationalViewMaterializationKind.Unknown;
    /// <summary>Gets or sets the refresh supported.</summary>
    public bool RefreshSupported { get; init; }
    /// <summary>Gets or sets the refresh status summary.</summary>
    public string? RefreshStatusSummary { get; init; }
    /// <summary>Gets or sets the last refreshed at.</summary>
    public DateTimeOffset? LastRefreshedAt { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; }
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}
