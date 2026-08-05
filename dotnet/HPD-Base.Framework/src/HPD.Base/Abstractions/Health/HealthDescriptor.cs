
namespace HPD.Base;

/// <summary>Represents a health descriptor.</summary>
public sealed record HealthDescriptor
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the scope.</summary>
    public required HealthScope Scope { get; init; }
    /// <summary>Gets or sets the target ref.</summary>
    public string? TargetRef { get; init; }
    /// <summary>Gets or sets the status.</summary>
    public HealthStatus Status { get; init; }
    /// <summary>Gets or sets the checked at.</summary>
    public DateTimeOffset CheckedAt { get; init; }
    /// <summary>Gets or sets the summary.</summary>
    public string? Summary { get; init; }
    /// <summary>Gets or sets the dependencies.</summary>
    public HealthDependency[]? Dependencies { get; init; }
    /// <summary>Gets or sets the metrics.</summary>
    public HealthMetric[]? Metrics { get; init; }
    /// <summary>Gets or sets the public safe.</summary>
    public bool PublicSafe { get; init; }
    /// <summary>Gets or sets the visibility.</summary>
    public VisibilityLevel Visibility { get; init; }
}

/// <summary>Represents a health dependency.</summary>
public sealed record HealthDependency
{
    /// <summary>Gets or sets the ID.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the kind.</summary>
    public required string Kind { get; init; }
    /// <summary>Gets or sets the status.</summary>
    public HealthStatus Status { get; init; }
}

/// <summary>Represents a health metric.</summary>
public sealed record HealthMetric
{
    /// <summary>Gets or sets the name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets or sets the kind.</summary>
    public required HealthMetricValueKind Kind { get; init; }
    /// <summary>Gets or sets the text value.</summary>
    public string? TextValue { get; init; }
    /// <summary>Gets or sets the number value.</summary>
    public double? NumberValue { get; init; }
    /// <summary>Gets or sets the boolean value.</summary>
    public bool? BooleanValue { get; init; }
    /// <summary>Gets or sets the unit.</summary>
    public string? Unit { get; init; }
}
