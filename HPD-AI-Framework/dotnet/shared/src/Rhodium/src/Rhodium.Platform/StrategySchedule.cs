using Rhodium.Primitives;

namespace Rhodium.Platform;

public readonly record struct StrategySchedule(
    string Name,
    bool IsRecurring,
    Duration Interval,
    Instant? FireAt,
    Instant? StopAt)
{
    public static StrategySchedule At(string name, Instant fireAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new StrategySchedule(name, IsRecurring: false, Duration.Zero, fireAt, StopAt: null);
    }

    public static StrategySchedule Every(
        string name,
        Duration interval,
        Instant? startAt = null,
        Instant? stopAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (interval <= Duration.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval), interval, "Recurring strategy schedule interval must be positive.");

        return new StrategySchedule(name, IsRecurring: true, interval, startAt, stopAt);
    }
}
