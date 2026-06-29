using System.Text.Json;
using HPD.Base;

namespace HPD.Base.Descriptors;

public sealed record BaseModuleDescriptor
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required BaseModuleKind Kind { get; init; }
    public required string Version { get; init; }
    public ModuleStatus Status { get; init; }
    public ModuleCompatibility? Compatibility { get; init; }
    public ModuleDependency[]? Dependencies { get; init; }
    public string[]? ContributedCapabilities { get; init; }
    public string[]? ContributedDtoIds { get; init; }
    public string[]? ContributedRouteIds { get; init; }
    public string[]? ContributedEventTypes { get; init; }
    public string[]? ContributedFieldAnnotationIds { get; init; }
    public string[]? ContributedHealthRefIds { get; init; }
    public string[]? ContributedDiagnosticIds { get; init; }
    public Dictionary<string, JsonElement>? PublicConfig { get; init; }
    public Dictionary<string, JsonElement>? AdminConfigSummary { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
}

public enum BaseModuleKind { Core, Store, Projection, Files, Realtime, LiveQuery, SchemaWrite, Policy, Relational, Search, Vector, Batch, Diagnostics, Custom }
public enum ModuleStatus { Installed, Disabled, Unavailable, Planned }

public sealed record ModuleCompatibility
{
    public required string RequiresBaseContract { get; init; }
    public string? RequiresRuntimeVersion { get; init; }
    public string[]? ProvidesContractVersions { get; init; }
}

public sealed record ModuleDependency
{
    public string? ModuleId { get; init; }
    public string? ModuleVersionRange { get; init; }
    public string? FeatureId { get; init; }
    public string? FeatureVersionRange { get; init; }
    public bool Required { get; init; } = true;
    public DependencyFailureBehavior FailureBehavior { get; init; } = DependencyFailureBehavior.RefuseStartup;
}

public enum DependencyFailureBehavior { RefuseStartup, DisableModule, DegradeFeature, AdvisoryDiagnostic }
