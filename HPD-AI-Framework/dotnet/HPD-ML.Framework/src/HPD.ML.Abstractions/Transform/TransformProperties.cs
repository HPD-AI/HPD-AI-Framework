namespace HPD.ML.Abstractions;

public sealed record TransformProperties
{
    public bool IsStateful { get; init; }
    public bool RequiresOrdering { get; init; }
    public bool PreservesRowCount { get; init; } = true;
    public DevicePreference? DevicePreference { get; init; }
    public ResourceRequirements? Resources { get; init; }
}

public sealed record DevicePreference(string? DeviceId, bool FallbackToCpu = true);

public sealed record ResourceRequirements(
    long? EstimatedMemoryBytes = null,
    int? MaxDegreeOfParallelism = null);
