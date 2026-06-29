using HPD.Base;

namespace HPD.Base.Descriptors;

public sealed record ProjectionDescriptor
{
    public required string Id { get; init; }
    public required ProjectionKind Kind { get; init; }
    public required string PackageId { get; init; }
    public required string PackageVersion { get; init; }
    public required string ContractVersionRange { get; init; }
    public ProjectionStatus Status { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    public string[]? RequiredCapabilities { get; init; }
    public string[]? ProvidedCapabilities { get; init; }
    public RouteDescriptor[]? Routes { get; init; }
    public DtoContractDescriptor[]? DtoContracts { get; init; }
    public ProjectionEntrypointDescriptor[]? Entrypoints { get; init; }
    public string[]? HealthRefs { get; init; }
    public string[]? DiagnosticRefs { get; init; }
}

public enum ProjectionKind { AspNet, TypeScriptSdk, Studio, GraphQl, OpenApi, Custom }
public enum ProjectionStatus { Available, Disabled, Unavailable, Preview }

public sealed record ProjectionEntrypointDescriptor
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required ProjectionEntrypointKind Kind { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    public string[]? RequiredFeatureIds { get; init; }
    public string[]? RouteRefs { get; init; }
}

public enum ProjectionEntrypointKind { Metadata, Records, Admin, Studio, Custom }
