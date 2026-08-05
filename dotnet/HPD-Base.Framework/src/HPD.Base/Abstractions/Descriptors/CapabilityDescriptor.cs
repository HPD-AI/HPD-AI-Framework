
namespace HPD.Base;

/// <summary>Represents a capability descriptor.</summary>
public sealed record CapabilityDescriptor
{
    /// <summary>Gets or sets the descriptor version.</summary>
    public required string DescriptorVersion { get; init; }
    /// <summary>Gets or sets the runtime ID.</summary>
    public required string RuntimeId { get; init; }
    /// <summary>Gets or sets the families.</summary>
    public required CapabilityFamilyDescriptor[] Families { get; init; }
}

/// <summary>Represents a capability family descriptor.</summary>
public sealed record CapabilityFamilyDescriptor
{
    /// <summary>Gets or sets the family ID.</summary>
    public required string FamilyId { get; init; }
    /// <summary>Gets or sets the family version.</summary>
    public required string FamilyVersion { get; init; }
    /// <summary>Gets or sets the status.</summary>
    public CapabilityStatus Status { get; init; }
    /// <summary>Gets or sets the owner module ID.</summary>
    public string? OwnerModuleId { get; init; }
    /// <summary>Gets or sets the scopes.</summary>
    public CapabilityScope[]? Scopes { get; init; }
    /// <summary>Gets or sets the features.</summary>
    public CapabilityFeatureDescriptor[]? Features { get; init; }
    /// <summary>Gets or sets the limits.</summary>
    public CapabilityLimitDescriptor[]? Limits { get; init; }
    /// <summary>Gets or sets the dependencies.</summary>
    public CapabilityDependencyDescriptor[]? Dependencies { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
}

/// <summary>Represents a capability feature descriptor.</summary>
public sealed record CapabilityFeatureDescriptor
{
    /// <summary>Gets or sets the feature ID.</summary>
    public required string FeatureId { get; init; }
    /// <summary>Gets or sets the version.</summary>
    public required string Version { get; init; }
    /// <summary>Gets or sets the status.</summary>
    public CapabilityStatus Status { get; init; }
    /// <summary>Gets or sets the support level.</summary>
    public SupportLevel SupportLevel { get; init; }
    /// <summary>Gets or sets the scope.</summary>
    public CapabilityScope Scope { get; init; }
    /// <summary>Gets or sets the applies to.</summary>
    public string[]? AppliesTo { get; init; }
    /// <summary>Gets or sets the constraints.</summary>
    public CapabilityConstraintSet? Constraints { get; init; }
    /// <summary>Gets or sets the DTO contracts.</summary>
    public string[]? DtoContracts { get; init; }
    /// <summary>Gets or sets the route refs.</summary>
    public string[]? RouteRefs { get; init; }
    /// <summary>Gets or sets the event type refs.</summary>
    public string[]? EventTypeRefs { get; init; }
    /// <summary>Gets or sets the health ref.</summary>
    public string? HealthRef { get; init; }
    /// <summary>Gets or sets the diagnostic refs.</summary>
    public string[]? DiagnosticRefs { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
}

/// <summary>Represents a capability limit descriptor.</summary>
public sealed record CapabilityLimitDescriptor
{
    /// <summary>Gets or sets the name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets or sets the value.</summary>
    public required string Value { get; init; }
    /// <summary>Gets or sets the unit.</summary>
    public string? Unit { get; init; }
}

/// <summary>Represents a capability dependency descriptor.</summary>
public sealed record CapabilityDependencyDescriptor
{
    /// <summary>Gets or sets the module ID.</summary>
    public string? ModuleId { get; init; }
    /// <summary>Gets or sets the feature ID.</summary>
    public string? FeatureId { get; init; }
    /// <summary>Gets or sets the version range.</summary>
    public string? VersionRange { get; init; }
    /// <summary>Gets or sets the required.</summary>
    public bool Required { get; init; } = true;
}
