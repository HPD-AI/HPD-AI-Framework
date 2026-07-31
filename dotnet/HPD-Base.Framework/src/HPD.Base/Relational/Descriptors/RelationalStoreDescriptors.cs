using System.Text.Json;

namespace HPD.Base;

public sealed record RelationalStoreDescriptor
{
    public required string Id { get; init; }
    public required string StoreId { get; init; }
    public required string DescriptorVersion { get; init; }
    public required RelationalProviderDescriptor Provider { get; init; }
    public RelationalDatabaseDescriptor[]? Databases { get; init; }
    public RelationalCatalogDescriptor[]? Catalogs { get; init; }
    public RelationalSchemaDescriptor[]? Schemas { get; init; }
    public RelationalTableDescriptor[]? Tables { get; init; }
    public RelationalViewDescriptor[]? Views { get; init; }
    public RelationalColumnDescriptor[]? Columns { get; init; }
    public RelationalPrimaryKeyDescriptor[]? PrimaryKeys { get; init; }
    public RelationalForeignKeyDescriptor[]? ForeignKeys { get; init; }
    public RelationalUniqueConstraintDescriptor[]? UniqueConstraints { get; init; }
    public RelationalCheckConstraintDescriptor[]? CheckConstraints { get; init; }
    public RelationalProviderConstraintDescriptor[]? ProviderConstraints { get; init; }
    public RelationalIndexDescriptor[]? Indexes { get; init; }
    public RelationalGeneratedColumnDescriptor[]? GeneratedColumns { get; init; }
    public RelationalJsonColumnDescriptor[]? JsonColumns { get; init; }
    public RelationalCollectionMappingDescriptor[]? CollectionMappings { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    public bool PublicSafe { get; init; } = true;
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

public sealed record RelationalProviderDescriptor
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? Version { get; init; }
    public string? EngineFamily { get; init; }
    public string? NativeSummary { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    public bool PublicSafe { get; init; } = true;
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}
