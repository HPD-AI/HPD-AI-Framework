namespace HPD.Base;

/// <summary>Represents a compatibility descriptor.</summary>
public sealed record CompatibilityDescriptor
{
    /// <summary>Gets or sets the base contract version.</summary>
    public required string BaseContractVersion { get; init; }
    /// <summary>Gets or sets the min client contract version.</summary>
    public required string MinClientContractVersion { get; init; }
    /// <summary>Gets or sets the max client contract version.</summary>
    public required string MaxClientContractVersion { get; init; }
    /// <summary>Gets or sets the breaking feature IDs.</summary>
    public string[]? BreakingFeatureIds { get; init; }
    /// <summary>Gets or sets the deprecated feature IDs.</summary>
    public string[]? DeprecatedFeatureIds { get; init; }
}
