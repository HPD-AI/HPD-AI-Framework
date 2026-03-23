using HPDAgent.Graph.Abstractions.Context;

namespace HPDAgent.Graph.Abstractions.Execution;

/// <summary>
/// Schedule constraint for edge traversal (Phase 4: Temporal Operators).
/// Allows declarative time-based routing without external schedulers.
/// Uses cron expressions for flexible scheduling.
/// </summary>
public sealed record ScheduleConstraint
{
    /// <summary>
    /// Cron expression defining when edge can be traversed.
    /// Format: "minute hour day month day-of-week"
    /// Examples:
    ///   - "0 3 * * *" = daily at 3am
    ///   - "0 0 * * 1" = weekly on Mondays at midnight
    ///   - "0 */6 * * *" = every 6 hours
    /// Uses Cronos library for parsing.
    /// </summary>
    public required string CronExpression { get; init; }

    /// <summary>
    /// Timezone for cron evaluation.
    /// Null = use UTC.
    /// Example: "America/New_York", "Europe/London"
    /// </summary>
    public TimeZoneInfo? TimeZone { get; init; }

    /// <summary>
    /// Tolerance window for schedule satisfaction.
    /// If current time is within tolerance of next scheduled time, edge can traverse.
    /// Default: 1 minute (allows slight timing variations).
    /// Example: Schedule is 3:00am, tolerance is 1 minute → edge can traverse from 2:59am to 3:01am
    /// </summary>
    public TimeSpan Tolerance { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Optional custom condition function for additional runtime checks.
    /// Evaluated AFTER cron schedule is satisfied.
    /// Example: Check if it's a business day, not a holiday, etc.
    /// </summary>
    public Func<IGraphContext, Task<bool>>? AdditionalCondition { get; init; }
}
