using HPD.Base;

namespace HPD.Base.Descriptors;

/// <summary>
/// Compact bootstrap document for BASE clients.
/// </summary>
public sealed record BaseManifest
{
    public required string ManifestVersion { get; init; }
    public required string ContractVersion { get; init; }
    public required RuntimeDescriptor Runtime { get; init; }
    public required CompatibilityDescriptor Compatibility { get; init; }
    public CollectionSummaryDescriptor[]? Collections { get; init; }
    public CapabilitySummaryDescriptor? Capabilities { get; init; }
    public BaseModuleDescriptor[]? Modules { get; init; }
    public ProjectionDescriptor[]? Projections { get; init; }
    public DtoContractDescriptor[]? DtoContracts { get; init; }
    public EventTypeDescriptor[]? EventTypes { get; init; }
    public HealthRefDescriptor[]? HealthRefs { get; init; }
    public DiagnosticRefDescriptor[]? DiagnosticRefs { get; init; }
    public ManifestLinkDescriptor[]? Links { get; init; }
    public required VisibilityLevel Visibility { get; init; }
    public string? ETag { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
}

public sealed record CollectionSummaryDescriptor
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? DisplayName { get; init; }
    public required string Kind { get; init; }
    public bool Enabled { get; init; } = true;
    public bool Exposed { get; init; } = true;
    public string? SchemaRef { get; init; }
    public string[]? RequiredFeatureIds { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
}

public sealed record CapabilitySummaryDescriptor
{
    public required string DescriptorVersion { get; init; }
    public required string RuntimeId { get; init; }
    public string[]? FamilyIds { get; init; }
    public string[]? FeatureIds { get; init; }
}
