
namespace HPD.Base;

/// <summary>Represents a projection descriptor.</summary>
public sealed record ProjectionDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the kind.</summary>
    public required ProjectionKind Kind { get; init; }
    /// <summary>Gets or sets the package ID.</summary>
    public required string PackageId { get; init; }
    /// <summary>Gets or sets the package version.</summary>
    public required string PackageVersion { get; init; }
    /// <summary>Gets or sets the contract version range.</summary>
    public required string ContractVersionRange { get; init; }
    /// <summary>Gets or sets the status.</summary>
    public ProjectionStatus Status { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    /// <summary>Gets or sets the required capabilities.</summary>
    public string[]? RequiredCapabilities { get; init; }
    /// <summary>Gets or sets the provided capabilities.</summary>
    public string[]? ProvidedCapabilities { get; init; }
    /// <summary>Gets or sets the routes.</summary>
    public RouteDescriptor[]? Routes { get; init; }
    /// <summary>Gets or sets the DTO contracts.</summary>
    public DtoContractDescriptor[]? DtoContracts { get; init; }
    /// <summary>Gets or sets the entrypoints.</summary>
    public ProjectionEntrypointDescriptor[]? Entrypoints { get; init; }
    /// <summary>Gets or sets the health refs.</summary>
    public string[]? HealthRefs { get; init; }
    /// <summary>Gets or sets the diagnostic refs.</summary>
    public string[]? DiagnosticRefs { get; init; }
}

/// <summary>Defines the projection kind contract.</summary>
public enum ProjectionKind { /// <summary>Identifies asp net.</summary>
AspNet, /// <summary>Identifies type script sdk.</summary>
TypeScriptSdk, /// <summary>Identifies studio.</summary>
Studio, /// <summary>Identifies graph ql.</summary>
GraphQl, /// <summary>Identifies open API.</summary>
OpenApi, /// <summary>Identifies custom.</summary>
Custom }
/// <summary>Defines the projection status contract.</summary>
public enum ProjectionStatus { /// <summary>Identifies available.</summary>
Available, /// <summary>Identifies disabled.</summary>
Disabled, /// <summary>Identifies unavailable.</summary>
Unavailable, /// <summary>Identifies preview.</summary>
Preview }

/// <summary>Represents a projection entrypoint descriptor.</summary>
public sealed record ProjectionEntrypointDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets or sets the kind.</summary>
    public required ProjectionEntrypointKind Kind { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    /// <summary>Gets or sets the required feature IDs.</summary>
    public string[]? RequiredFeatureIds { get; init; }
    /// <summary>Gets or sets the route refs.</summary>
    public string[]? RouteRefs { get; init; }
}

/// <summary>Defines the projection entrypoint kind contract.</summary>
public enum ProjectionEntrypointKind { /// <summary>Identifies metadata.</summary>
Metadata, /// <summary>Identifies records.</summary>
Records, /// <summary>Identifies admin.</summary>
Admin, /// <summary>Identifies studio.</summary>
Studio, /// <summary>Identifies custom.</summary>
Custom }
