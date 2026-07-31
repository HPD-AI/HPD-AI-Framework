
namespace HPD.Base;

public sealed record CapabilityDescriptor
{
    public required string DescriptorVersion { get; init; }
    public required string RuntimeId { get; init; }
    public required CapabilityFamilyDescriptor[] Families { get; init; }
}

public sealed record CapabilityFamilyDescriptor
{
    public required string FamilyId { get; init; }
    public required string FamilyVersion { get; init; }
    public CapabilityStatus Status { get; init; }
    public string? OwnerModuleId { get; init; }
    public CapabilityScope[]? Scopes { get; init; }
    public CapabilityFeatureDescriptor[]? Features { get; init; }
    public CapabilityLimitDescriptor[]? Limits { get; init; }
    public CapabilityDependencyDescriptor[]? Dependencies { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
}

public sealed record CapabilityFeatureDescriptor
{
    public required string FeatureId { get; init; }
    public required string Version { get; init; }
    public CapabilityStatus Status { get; init; }
    public SupportLevel SupportLevel { get; init; }
    public CapabilityScope Scope { get; init; }
    public string[]? AppliesTo { get; init; }
    public CapabilityConstraintSet? Constraints { get; init; }
    public string[]? DtoContracts { get; init; }
    public string[]? RouteRefs { get; init; }
    public string[]? EventTypeRefs { get; init; }
    public string? HealthRef { get; init; }
    public string[]? DiagnosticRefs { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
}

public sealed record CapabilityLimitDescriptor
{
    public required string Name { get; init; }
    public required string Value { get; init; }
    public string? Unit { get; init; }
}

public sealed record CapabilityDependencyDescriptor
{
    public string? ModuleId { get; init; }
    public string? FeatureId { get; init; }
    public string? VersionRange { get; init; }
    public bool Required { get; init; } = true;
}
