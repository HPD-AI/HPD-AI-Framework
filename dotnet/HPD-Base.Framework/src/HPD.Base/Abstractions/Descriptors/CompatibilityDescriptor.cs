namespace HPD.Base.Descriptors;

public sealed record CompatibilityDescriptor
{
    public required string BaseContractVersion { get; init; }
    public required string MinClientContractVersion { get; init; }
    public required string MaxClientContractVersion { get; init; }
    public string[]? BreakingFeatureIds { get; init; }
    public string[]? DeprecatedFeatureIds { get; init; }
}
