
namespace HPD.Base;

public sealed record SchemaMetadata
{
    public required string RuntimeId { get; init; }
    public required string ContractVersion { get; init; }
    public required VisibilityLevel Visibility { get; init; }
    public SchemaMetadataRole Role { get; init; } = SchemaMetadataRole.ReadProjection;
    public CollectionDefinition[]? Collections { get; init; }
    public SchemaRelationSummary[]? Relations { get; init; }
    public SchemaSourceDescriptor[]? Sources { get; init; }
    public DiagnosticDescriptor[]? Diagnostics { get; init; }
    public string[]? Capabilities { get; init; }
    public string? ETag { get; init; }
    public DateTimeOffset? RefreshedAt { get; init; }
}

public sealed record SchemaSourceDescriptor
{
    public required string Id { get; init; }
    public required SchemaSourceKind Kind { get; init; }
    public string? OwnerModuleId { get; init; }
    public string? StoreId { get; init; }
    public string? Version { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
}

public sealed record SchemaRelationSummary
{
    public required string Id { get; init; }
    public required string SourceCollectionId { get; init; }
    public required string SourceFieldPath { get; init; }
    public required string TargetCollectionId { get; init; }
    public string? TargetFieldPath { get; init; }
    public RelationKind Kind { get; init; }
    public RelationCardinality Cardinality { get; init; }
    public VisibilityLevel Visibility { get; init; }
}
