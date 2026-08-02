using System.Text.Json;

namespace HPD.Base;

/// <summary>Represents a base module descriptor.</summary>
public sealed record BaseModuleDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets or sets the kind.</summary>
    public required BaseModuleKind Kind { get; init; }
    /// <summary>Gets or sets the version.</summary>
    public required string Version { get; init; }
    /// <summary>Gets or sets the status.</summary>
    public ModuleStatus Status { get; init; }
    /// <summary>Gets or sets the compatibility.</summary>
    public ModuleCompatibility? Compatibility { get; init; }
    /// <summary>Gets or sets the dependencies.</summary>
    public ModuleDependency[]? Dependencies { get; init; }
    /// <summary>Gets or sets the contributed capabilities.</summary>
    public string[]? ContributedCapabilities { get; init; }
    /// <summary>Gets or sets the contributed DTO IDs.</summary>
    public string[]? ContributedDtoIds { get; init; }
    /// <summary>Gets or sets the contributed route IDs.</summary>
    public string[]? ContributedRouteIds { get; init; }
    /// <summary>Gets or sets the contributed event types.</summary>
    public string[]? ContributedEventTypes { get; init; }
    /// <summary>Gets or sets the contributed field annotation IDs.</summary>
    public string[]? ContributedFieldAnnotationIds { get; init; }
    /// <summary>Gets or sets the contributed health ref IDs.</summary>
    public string[]? ContributedHealthRefIds { get; init; }
    /// <summary>Gets or sets the contributed diagnostic IDs.</summary>
    public string[]? ContributedDiagnosticIds { get; init; }
    /// <summary>Gets or sets the public config.</summary>
    public Dictionary<string, JsonElement>? PublicConfig { get; init; }
    /// <summary>Gets or sets the admin config summary.</summary>
    public Dictionary<string, JsonElement>? AdminConfigSummary { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
}

/// <summary>Defines the base module kind contract.</summary>
public enum BaseModuleKind { /// <summary>Identifies core.</summary>
Core, /// <summary>Identifies store.</summary>
Store, /// <summary>Identifies projection.</summary>
Projection, /// <summary>Identifies files.</summary>
Files, /// <summary>Identifies realtime.</summary>
Realtime, /// <summary>Identifies live query.</summary>
LiveQuery, /// <summary>Identifies schema write.</summary>
SchemaWrite, /// <summary>Identifies policy.</summary>
Policy, /// <summary>Identifies relational.</summary>
Relational, /// <summary>Identifies search.</summary>
Search, /// <summary>Identifies vector.</summary>
Vector, /// <summary>Identifies batch.</summary>
Batch, /// <summary>Identifies diagnostics.</summary>
Diagnostics, /// <summary>Identifies custom.</summary>
Custom }
/// <summary>Defines the module status contract.</summary>
public enum ModuleStatus { /// <summary>Identifies installed.</summary>
Installed, /// <summary>Identifies disabled.</summary>
Disabled, /// <summary>Identifies unavailable.</summary>
Unavailable, /// <summary>Identifies planned.</summary>
Planned }

/// <summary>Represents a module compatibility.</summary>
public sealed record ModuleCompatibility
{
    /// <summary>Gets or sets the requires base contract.</summary>
    public required string RequiresBaseContract { get; init; }
    /// <summary>Gets or sets the requires runtime version.</summary>
    public string? RequiresRuntimeVersion { get; init; }
    /// <summary>Gets or sets the provides contract versions.</summary>
    public string[]? ProvidesContractVersions { get; init; }
}

/// <summary>Represents a module dependency.</summary>
public sealed record ModuleDependency
{
    /// <summary>Gets or sets the module ID.</summary>
    public string? ModuleId { get; init; }
    /// <summary>Gets or sets the module version range.</summary>
    public string? ModuleVersionRange { get; init; }
    /// <summary>Gets or sets the feature ID.</summary>
    public string? FeatureId { get; init; }
    /// <summary>Gets or sets the feature version range.</summary>
    public string? FeatureVersionRange { get; init; }
    /// <summary>Gets or sets the required.</summary>
    public bool Required { get; init; } = true;
    /// <summary>Gets or sets the failure behavior.</summary>
    public DependencyFailureBehavior FailureBehavior { get; init; } = DependencyFailureBehavior.RefuseStartup;
}

/// <summary>Defines the dependency failure behavior contract.</summary>
public enum DependencyFailureBehavior { /// <summary>Identifies refuse startup.</summary>
RefuseStartup, /// <summary>Identifies disable module.</summary>
DisableModule, /// <summary>Identifies degrade feature.</summary>
DegradeFeature, /// <summary>Identifies advisory diagnostic.</summary>
AdvisoryDiagnostic }
