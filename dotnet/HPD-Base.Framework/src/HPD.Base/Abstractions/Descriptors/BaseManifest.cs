
namespace HPD.Base;

/// <summary>
/// Compact bootstrap document for BASE clients.
/// </summary>
public sealed record BaseManifest
{
    /// <summary>Gets or sets manifest Version.</summary>
    public required string ManifestVersion { get; init; }
    /// <summary>Gets or sets contract Version.</summary>
    public required string ContractVersion { get; init; }
    /// <summary>Gets or sets runtime.</summary>
    public required RuntimeDescriptor Runtime { get; init; }
    /// <summary>Gets or sets compatibility.</summary>
    public required CompatibilityDescriptor Compatibility { get; init; }
    /// <summary>Gets or sets collections.</summary>
    public CollectionSummaryDescriptor[]? Collections { get; init; }
    /// <summary>Gets or sets capabilities.</summary>
    public CapabilitySummaryDescriptor? Capabilities { get; init; }
    /// <summary>Gets or sets modules.</summary>
    public BaseModuleDescriptor[]? Modules { get; init; }
    /// <summary>Gets or sets projections.</summary>
    public ProjectionDescriptor[]? Projections { get; init; }
    /// <summary>Gets or sets dto Contracts.</summary>
    public DtoContractDescriptor[]? DtoContracts { get; init; }
    /// <summary>Gets or sets event Types.</summary>
    public EventTypeDescriptor[]? EventTypes { get; init; }
    /// <summary>Gets or sets health Refs.</summary>
    public HealthRefDescriptor[]? HealthRefs { get; init; }
    /// <summary>Gets or sets diagnostic Refs.</summary>
    public DiagnosticRefDescriptor[]? DiagnosticRefs { get; init; }
    /// <summary>Gets or sets links.</summary>
    public ManifestLinkDescriptor[]? Links { get; init; }
    /// <summary>Gets or sets visibility.</summary>
    public required VisibilityLevel Visibility { get; init; }
    /// <summary>Gets or sets eTag.</summary>
    public string? ETag { get; init; }
    /// <summary>Gets or sets generated At.</summary>
    public DateTimeOffset GeneratedAt { get; init; }
}

/// <summary>Represents collection Summary Descriptor.</summary>
public sealed record CollectionSummaryDescriptor
{
    /// <summary>Gets or sets id.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets or sets display Name.</summary>
    public string? DisplayName { get; init; }
    /// <summary>Gets or sets kind.</summary>
    public required string Kind { get; init; }
    /// <summary>Gets or sets enabled.</summary>
    public bool Enabled { get; init; } = true;
    /// <summary>Gets or sets exposed.</summary>
    public bool Exposed { get; init; } = true;
    /// <summary>Gets or sets schema Ref.</summary>
    public string? SchemaRef { get; init; }
    /// <summary>Gets or sets required Feature Ids.</summary>
    public string[]? RequiredFeatureIds { get; init; }
    /// <summary>Gets or sets visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
}

/// <summary>Represents capability Summary Descriptor.</summary>
public sealed record CapabilitySummaryDescriptor
{
    /// <summary>Gets or sets descriptor Version.</summary>
    public required string DescriptorVersion { get; init; }
    /// <summary>Gets or sets runtime Id.</summary>
    public required string RuntimeId { get; init; }
    /// <summary>Gets or sets family Ids.</summary>
    public string[]? FamilyIds { get; init; }
    /// <summary>Gets or sets feature Ids.</summary>
    public string[]? FeatureIds { get; init; }
}
