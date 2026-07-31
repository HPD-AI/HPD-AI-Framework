
namespace HPD.Base;

public sealed record HealthDescriptor
{
    public required string Id { get; init; }
    public required HealthScope Scope { get; init; }
    public string? TargetRef { get; init; }
    public HealthStatus Status { get; init; }
    public DateTimeOffset CheckedAt { get; init; }
    public string? Summary { get; init; }
    public HealthDependency[]? Dependencies { get; init; }
    public HealthMetric[]? Metrics { get; init; }
    public bool PublicSafe { get; init; }
    public VisibilityLevel Visibility { get; init; }
}

public sealed record HealthDependency
{
    public required string Id { get; init; }
    public required string Kind { get; init; }
    public HealthStatus Status { get; init; }
}

public sealed record HealthMetric
{
    public required string Name { get; init; }
    public required HealthMetricValueKind Kind { get; init; }
    public string? TextValue { get; init; }
    public double? NumberValue { get; init; }
    public bool? BooleanValue { get; init; }
    public string? Unit { get; init; }
}
