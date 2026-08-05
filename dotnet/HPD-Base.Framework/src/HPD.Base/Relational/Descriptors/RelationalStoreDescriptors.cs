using System.Text.Json;

namespace HPD.Base;

/// <summary>Represents a relational store descriptor.</summary>
public sealed record RelationalStoreDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the store ID.</summary>
    public required string StoreId { get; init; }
    /// <summary>Gets or sets the descriptor version.</summary>
    public required string DescriptorVersion { get; init; }
    /// <summary>Gets or sets the provider.</summary>
    public required RelationalProviderDescriptor Provider { get; init; }
    /// <summary>Gets or sets the databases.</summary>
    public RelationalDatabaseDescriptor[]? Databases { get; init; }
    /// <summary>Gets or sets the catalogs.</summary>
    public RelationalCatalogDescriptor[]? Catalogs { get; init; }
    /// <summary>Gets or sets the schemas.</summary>
    public RelationalSchemaDescriptor[]? Schemas { get; init; }
    /// <summary>Gets or sets the tables.</summary>
    public RelationalTableDescriptor[]? Tables { get; init; }
    /// <summary>Gets or sets the views.</summary>
    public RelationalViewDescriptor[]? Views { get; init; }
    /// <summary>Gets or sets the columns.</summary>
    public RelationalColumnDescriptor[]? Columns { get; init; }
    /// <summary>Gets or sets the primary keys.</summary>
    public RelationalPrimaryKeyDescriptor[]? PrimaryKeys { get; init; }
    /// <summary>Gets or sets the foreign keys.</summary>
    public RelationalForeignKeyDescriptor[]? ForeignKeys { get; init; }
    /// <summary>Gets or sets the unique constraints.</summary>
    public RelationalUniqueConstraintDescriptor[]? UniqueConstraints { get; init; }
    /// <summary>Gets or sets the check constraints.</summary>
    public RelationalCheckConstraintDescriptor[]? CheckConstraints { get; init; }
    /// <summary>Gets or sets the provider constraints.</summary>
    public RelationalProviderConstraintDescriptor[]? ProviderConstraints { get; init; }
    /// <summary>Gets or sets the indexes.</summary>
    public RelationalIndexDescriptor[]? Indexes { get; init; }
    /// <summary>Gets or sets the generated columns.</summary>
    public RelationalGeneratedColumnDescriptor[]? GeneratedColumns { get; init; }
    /// <summary>Gets or sets the JSON columns.</summary>
    public RelationalJsonColumnDescriptor[]? JsonColumns { get; init; }
    /// <summary>Gets or sets the collection mappings.</summary>
    public RelationalCollectionMappingDescriptor[]? CollectionMappings { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; } = true;
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

/// <summary>Represents a relational provider descriptor.</summary>
public sealed record RelationalProviderDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets or sets the version.</summary>
    public string? Version { get; init; }
    /// <summary>Gets or sets the engine family.</summary>
    public string? EngineFamily { get; init; }
    /// <summary>Gets or sets the native summary.</summary>
    public string? NativeSummary { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; } = true;
    /// <summary>Gets or sets the extensions.</summary>
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}
