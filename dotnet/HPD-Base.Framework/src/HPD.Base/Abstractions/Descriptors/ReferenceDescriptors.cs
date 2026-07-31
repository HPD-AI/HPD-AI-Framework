
namespace HPD.Base;

public sealed record EventTypeDescriptor
{
    public required string Type { get; init; }
    public required string EnvelopeVersion { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
    public string? SchemaId { get; init; }
    public string[]? ChannelPatterns { get; init; }
}

public sealed record HealthRefDescriptor
{
    public required string Id { get; init; }
    public required HealthScope Scope { get; init; }
    public string? TargetRef { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Public;
}

public sealed record DiagnosticRefDescriptor
{
    public required string Id { get; init; }
    public VisibilityLevel Visibility { get; init; } = VisibilityLevel.Admin;
}

public sealed record FieldAnnotationDescriptor
{
    public required string Id { get; init; }
    public required string ModuleId { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
}
