using System.Text.Json;

namespace HPD.Graph.Abstractions.Config;

public sealed record GraphScheduleConfig
{
    public required string CronExpression { get; init; }
    public string TimeZoneId { get; init; } = "UTC";
    public string? Description { get; init; }
    public int MaxRetries { get; init; }
    public TimeSpan? RetryAfter { get; init; }
    public TimeSpan? Timeout { get; init; }
    public ScheduleMisfirePolicyConfig MisfirePolicy { get; init; } = ScheduleMisfirePolicyConfig.Skip;
    public ScheduleConcurrencyPolicyConfig ConcurrencyPolicy { get; init; } = ScheduleConcurrencyPolicyConfig.SkipIfRunning;
    public JsonElement? DefaultInput { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public enum ScheduleMisfirePolicyConfig
{
    Skip,
    RunOnce,
    RunAllMissed
}

public enum ScheduleConcurrencyPolicyConfig
{
    AllowOverlap,
    SkipIfRunning,
    Queue,
    CancelPrevious
}
