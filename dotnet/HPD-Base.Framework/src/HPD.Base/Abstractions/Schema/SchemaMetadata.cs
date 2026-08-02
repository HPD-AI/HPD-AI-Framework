namespace HPD.Base;
/// <summary>Represents schema Metadata.</summary>
public sealed record SchemaMetadata
{
    /// <summary>Gets or sets runtime Id.</summary>
    public required string RuntimeId { get; init; }
    /// <summary>Gets or sets contract Version.</summary>
    public required string ContractVersion { get; init; }
    /// <summary>Gets or sets visibility.</summary>
    public required VisibilityLevel Visibility { get; init; }
    /// <summary>Gets or sets role.</summary>
    public SchemaMetadataRole Role { get; init; } = SchemaMetadataRole.ReadProjection;
    /// <summary>Gets or sets collections.</summary>
    public CollectionDefinition[]? Collections { get; init; }
    /// <summary>Gets or sets relations.</summary>
    public SchemaRelationSummary[]? Relations { get; init; }
    /// <summary>Gets or sets sources.</summary>
    public SchemaSourceDescriptor[]? Sources { get; init; }
    /// <summary>Gets or sets diagnostics.</summary>
    public DiagnosticDescriptor[]? Diagnostics { get; init; }
    /// <summary>Gets or sets capabilities.</summary>
    public string[]? Capabilities { get; init; }
    /// <summary>Gets or sets eTag.</summary>
    public string? ETag { get; init; }
    /// <summary>Gets or sets refreshed At.</summary>
    public DateTimeOffset? RefreshedAt { get; init; }
}

/// <summary>Represents schema Source Descriptor.</summary>
public sealed record SchemaSourceDescriptor
{
    /// <summary>Gets or sets id.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets kind.</summary>
    public required SchemaSourceKind Kind { get; init; }
    /// <summary>Gets or sets owner Module Id.</summary>
    public string? OwnerModuleId { get; init; }
    /// <summary>Gets or sets store Id.</summary>
    public string? StoreId { get; init; }
    /// <summary>Gets or sets version.</summary>
    public string? Version { get; init; }
    /// <summary>Gets or sets visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
}

/// <summary>Represents schema Relation Summary.</summary>
public sealed record SchemaRelationSummary
{
    /// <summary>Gets or sets id.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets source Collection Id.</summary>
    public required string SourceCollectionId { get; init; }
    /// <summary>Gets or sets source Field Id.</summary>
    public required string SourceFieldId { get; init; }
    /// <summary>Gets or sets target Collection Id.</summary>
    public required string TargetCollectionId { get; init; }
    /// <summary>Gets or sets target Field Id.</summary>
    public required string TargetFieldId { get; init; }
    /// <summary>Gets or sets local Multiplicity.</summary>
    public BaseRelationMultiplicity LocalMultiplicity { get; init; }
    /// <summary>Gets or sets inverse Multiplicity.</summary>
    public BaseRelationMultiplicity InverseMultiplicity { get; init; }
    /// <summary>Gets or sets visibility.</summary>
    public VisibilityLevel Visibility { get; init; }
}
