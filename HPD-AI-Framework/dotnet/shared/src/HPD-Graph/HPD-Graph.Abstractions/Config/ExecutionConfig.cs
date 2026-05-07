using System.Text.Json;

namespace HPDAgent.Graph.Abstractions.Config;

public sealed record RetryPolicyConfig
{
    public required int MaxAttempts { get; init; }
    public required TimeSpan InitialDelay { get; init; }
    public BackoffStrategyConfig Strategy { get; init; } = BackoffStrategyConfig.Exponential;
    public TimeSpan? MaxDelay { get; init; }
    public IReadOnlyList<string>? RetryableExceptionTypeNames { get; init; }
}

public enum BackoffStrategyConfig
{
    Constant,
    Exponential,
    Linear,
    JitteredExponential
}

public sealed record EdgeRetryPolicyConfig
{
    public required TimeSpan RetryInterval { get; init; }
    public TimeSpan? MaxWaitTime { get; init; }
    public int? MaxRetries { get; init; }
    public EdgeRetryExhaustedBehaviorConfig ExhaustedBehavior { get; init; } =
        EdgeRetryExhaustedBehaviorConfig.FailGraph;
}

public enum EdgeRetryExhaustedBehaviorConfig
{
    FailGraph,
    SkipNode
}

public sealed record ScheduleConstraintConfig
{
    public required string CronExpression { get; init; }
    public string? TimeZoneId { get; init; }
    public TimeSpan Tolerance { get; init; } = TimeSpan.FromMinutes(1);
}

public sealed record SuspensionOptionsConfig
{
    public TimeSpan ActiveWaitTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public bool EmitEvents { get; init; } = true;
    public bool SaveCheckpointFirst { get; init; } = true;
}

public sealed record ErrorPropagationPolicyConfig
{
    public required PropagationModeConfig Mode { get; init; }
    public IReadOnlyList<string>? AffectedNodes { get; init; }
    public string? FallbackNodeId { get; init; }
}

public enum PropagationModeConfig
{
    StopGraph,
    SkipDependents,
    ExecuteFallback,
    Isolate
}

public enum CloningPolicyConfig
{
    AlwaysClone,
    NeverClone,
    LazyClone
}

public sealed record IterationOptionsConfig
{
    public int MaxIterations { get; init; } = 10;
    public bool EnableChangeDetection { get; init; }
    public bool StopOnConvergence { get; init; }
    public TimeSpan? IterationTimeout { get; init; }
}

public sealed record ArtifactDependencyConfig
{
    public string? ProducesArtifact { get; init; }
    public IReadOnlyList<string>? RequiresArtifacts { get; init; }
}

public sealed record PartitionDefinitionConfig
{
    public required PartitionKindConfig Type { get; init; }
    public JsonElement? Definition { get; init; }
}

public enum PartitionKindConfig
{
    Static,
    Time,
    Multi
}

public sealed record PartitionDependencyConfig
{
    public PartitionDependencyMappingKindConfig? Type { get; init; }
    public CustomPrimitiveDescriptorConfig? Custom { get; init; }
    public JsonElement? Mapping { get; init; }
}

public enum PartitionDependencyMappingKindConfig
{
    WeeklyFromDaily,
    MonthlyFromDaily,
    QuarterlyFromMonthly,
    YearlyFromMonthly
}

public sealed record CacheOptionsConfig
{
    public bool Enabled { get; init; } = true;
    public string? Strategy { get; init; }
    public TimeSpan? Ttl { get; init; }
    public string? Invalidation { get; init; }
}

public sealed record InputSchemaConfig
{
    public required string TypeName { get; init; }
    public bool Required { get; init; } = true;
    public JsonElement? Constraints { get; init; }
}

public sealed record CustomPrimitiveDescriptorConfig
{
    public required string Name { get; init; }
    public JsonElement? Arguments { get; init; }
}
