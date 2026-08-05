
namespace HPD.Base;

/// <summary>Represents a event type descriptor.</summary>
public sealed record EventTypeDescriptor
{
    /// <summary>Gets or sets the type.</summary>
    public required string Type { get; init; }
    /// <summary>Gets or sets the envelope version.</summary>
    public required string EnvelopeVersion { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    /// <summary>Gets or sets the schema ID.</summary>
    public string? SchemaId { get; init; }
    /// <summary>Gets or sets the channel patterns.</summary>
    public string[]? ChannelPatterns { get; init; }
}

/// <summary>Represents a health ref descriptor.</summary>
public sealed record HealthRefDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the scope.</summary>
    public required HealthScope Scope { get; init; }
    /// <summary>Gets or sets the target ref.</summary>
    public string? TargetRef { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
}

/// <summary>Represents a diagnostic ref descriptor.</summary>
public sealed record DiagnosticRefDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
}

/// <summary>Represents a field annotation descriptor.</summary>
public sealed record FieldAnnotationDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the module ID.</summary>
    public required string ModuleId { get; init; }
    /// <summary>Gets or sets the name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets or sets the version.</summary>
    public required string Version { get; init; }
}
