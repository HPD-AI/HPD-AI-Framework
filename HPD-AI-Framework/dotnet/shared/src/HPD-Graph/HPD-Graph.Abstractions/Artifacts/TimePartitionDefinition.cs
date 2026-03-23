using System.Globalization;
using System.IO.Hashing;
using System.Text;

namespace HPDAgent.Graph.Abstractions.Artifacts;

/// <summary>
/// Time-based partitioning (hourly, daily, weekly, monthly, quarterly, yearly).
/// Generates partition keys based on calendar boundaries.
/// </summary>
public record TimePartitionDefinition : PartitionDefinition
{
    /// <summary>
    /// Granularity of time-based partitioning.
    /// </summary>
    public enum Granularity
    {
        /// <summary>Hourly partitions (2024-01-15-14)</summary>
        Hourly,

        /// <summary>Daily partitions (2024-01-15)</summary>
        Daily,

        /// <summary>Weekly partitions (2024-W03, ISO week format)</summary>
        Weekly,

        /// <summary>Monthly partitions (2024-01)</summary>
        Monthly,

        /// <summary>Quarterly partitions (2024-Q1)</summary>
        Quarterly,

        /// <summary>Yearly partitions (2024)</summary>
        Yearly
    }

    /// <summary>
    /// Time interval for partitions.
    /// </summary>
    public required Granularity Interval { get; init; }

    /// <summary>
    /// Start of the partition range.
    /// </summary>
    public required DateTimeOffset Start { get; init; }

    /// <summary>
    /// End of the partition range (null = unbounded).
    /// </summary>
    public DateTimeOffset? End { get; init; }

    /// <summary>
    /// Timezone for partition boundaries.
    /// Default is UTC.
    /// </summary>
    public string Timezone { get; init; } = "UTC";

    public override Task<PartitionSnapshot> ResolveAsync(
        IServiceProvider services,
        CancellationToken cancellationToken)
    {
        // Generate time partitions (synchronous operation)
        var partitionKeys = GenerateTimePartitions().ToList();

        // Compute deterministic hash: same time range + interval = same hash
        var snapshotHash = ComputeStableHash();

        var snapshot = new PartitionSnapshot
        {
            Keys = partitionKeys,
            SnapshotHash = snapshotHash
        };

        return Task.FromResult(snapshot);
    }

    /// <summary>
    /// Generate time partitions based on interval and range.
    /// Internal helper method used by ResolveAsync.
    /// </summary>
    private IEnumerable<PartitionKey> GenerateTimePartitions()
    {
        var effectiveEnd = End ?? DateTimeOffset.UtcNow;
        var tz = TimeZoneInfo.FindSystemTimeZoneById(Timezone);

        // Convert to local time in target timezone
        var localStart = TimeZoneInfo.ConvertTime(Start, tz);
        var localEnd = TimeZoneInfo.ConvertTime(effectiveEnd, tz);

        // Round start down to partition boundary
        var roundedStart = RoundDownToPartitionBoundary(localStart, tz);

        // If rounding went before the original start, advance to the partition containing start
        var current = roundedStart < localStart ? AdvanceToNextPartition(roundedStart) : roundedStart;

        while (current < localEnd)
        {
            yield return FormatPartitionKey(current, tz);

            // Advance to next partition
            current = AdvanceToNextPartition(current);
        }
    }

    /// <summary>
    /// Advance to the next partition boundary based on the interval.
    /// </summary>
    private DateTimeOffset AdvanceToNextPartition(DateTimeOffset current)
    {
        return Interval switch
        {
            Granularity.Hourly => current.AddHours(1),
            Granularity.Daily => current.AddDays(1),
            Granularity.Weekly => current.AddDays(7),
            Granularity.Monthly => current.AddMonths(1),
            Granularity.Quarterly => current.AddMonths(3),
            Granularity.Yearly => current.AddYears(1),
            _ => throw new InvalidOperationException($"Unknown granularity: {Interval}")
        };
    }

    /// <summary>
    /// Compute stable hash from time partition configuration.
    /// Hash includes interval, start, end, and timezone - all configuration that determines the partition set.
    /// </summary>
    private string ComputeStableHash()
    {
        var hash = new XxHash64();

        // Include type discriminator and interval
        hash.Append(Encoding.UTF8.GetBytes($"Time:{Interval}"));

        // Include time range (deterministic)
        hash.Append(Encoding.UTF8.GetBytes(Start.ToString("O")));
        hash.Append(Encoding.UTF8.GetBytes((End ?? DateTimeOffset.MaxValue).ToString("O")));

        // Include timezone
        hash.Append(Encoding.UTF8.GetBytes(Timezone));

        return Convert.ToHexString(hash.GetCurrentHash());
    }

    /// <summary>
    /// Round down a timestamp to the start of its partition.
    /// </summary>
    private DateTimeOffset RoundDownToPartitionBoundary(DateTimeOffset time, TimeZoneInfo tz)
    {
        var offset = tz.GetUtcOffset(time);

        return Interval switch
        {
            Granularity.Hourly => new DateTimeOffset(
                time.Year, time.Month, time.Day, time.Hour, 0, 0, offset),

            Granularity.Daily => new DateTimeOffset(
                time.Year, time.Month, time.Day, 0, 0, 0, offset),

            Granularity.Weekly => RoundToWeekStart(time, tz),

            Granularity.Monthly => new DateTimeOffset(
                time.Year, time.Month, 1, 0, 0, 0, offset),

            Granularity.Quarterly => RoundToQuarterStart(time, tz),

            Granularity.Yearly => new DateTimeOffset(
                time.Year, 1, 1, 0, 0, 0, offset),

            _ => throw new InvalidOperationException($"Unknown granularity: {Interval}")
        };
    }

    /// <summary>
    /// Round down to the start of the ISO week (Monday).
    /// </summary>
    private DateTimeOffset RoundToWeekStart(DateTimeOffset time, TimeZoneInfo tz)
    {
        var offset = tz.GetUtcOffset(time);

        // ISO 8601: Week starts on Monday
        var daysToSubtract = ((int)time.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        var weekStart = time.Date.AddDays(-daysToSubtract);

        return new DateTimeOffset(
            weekStart.Year, weekStart.Month, weekStart.Day, 0, 0, 0, offset);
    }

    /// <summary>
    /// Round down to the start of the quarter (Q1=Jan, Q2=Apr, Q3=Jul, Q4=Oct).
    /// </summary>
    private DateTimeOffset RoundToQuarterStart(DateTimeOffset time, TimeZoneInfo tz)
    {
        var offset = tz.GetUtcOffset(time);
        var quarterStartMonth = ((time.Month - 1) / 3) * 3 + 1;

        return new DateTimeOffset(
            time.Year, quarterStartMonth, 1, 0, 0, 0, offset);
    }

    /// <summary>
    /// Format a partition key for the given timestamp.
    /// </summary>
    private PartitionKey FormatPartitionKey(DateTimeOffset time, TimeZoneInfo tz)
    {
        var localTime = TimeZoneInfo.ConvertTime(time, tz);

        var formatted = Interval switch
        {
            Granularity.Hourly => localTime.ToString("yyyy-MM-dd-HH", CultureInfo.InvariantCulture),
            Granularity.Daily => localTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Granularity.Weekly => FormatIsoWeek(localTime),
            Granularity.Monthly => localTime.ToString("yyyy-MM", CultureInfo.InvariantCulture),
            Granularity.Quarterly => FormatQuarter(localTime),
            Granularity.Yearly => localTime.ToString("yyyy", CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Unknown granularity: {Interval}")
        };

        return new PartitionKey { Dimensions = new[] { formatted } };
    }

    /// <summary>
    /// Format ISO week: "2024-W03"
    /// </summary>
    private string FormatIsoWeek(DateTimeOffset time)
    {
        var calendar = CultureInfo.InvariantCulture.Calendar;
        var weekOfYear = calendar.GetWeekOfYear(
            time.DateTime,
            CalendarWeekRule.FirstFourDayWeek,
            DayOfWeek.Monday);

        return $"{time.Year:D4}-W{weekOfYear:D2}";
    }

    /// <summary>
    /// Format quarter: "2024-Q1"
    /// </summary>
    private string FormatQuarter(DateTimeOffset time)
    {
        var quarter = (time.Month - 1) / 3 + 1;
        return $"{time.Year:D4}-Q{quarter}";
    }

    /// <summary>
    /// Factory: Create daily partitions starting from a date.
    /// </summary>
    public static TimePartitionDefinition Daily(DateTimeOffset start, DateTimeOffset? end = null, string timezone = "UTC")
    {
        return new TimePartitionDefinition
        {
            Interval = Granularity.Daily,
            Start = start,
            End = end,
            Timezone = timezone
        };
    }

    /// <summary>
    /// Factory: Create weekly partitions starting from a date.
    /// </summary>
    public static TimePartitionDefinition Weekly(DateTimeOffset start, DateTimeOffset? end = null, string timezone = "UTC")
    {
        return new TimePartitionDefinition
        {
            Interval = Granularity.Weekly,
            Start = start,
            End = end,
            Timezone = timezone
        };
    }

    /// <summary>
    /// Factory: Create monthly partitions starting from a date.
    /// </summary>
    public static TimePartitionDefinition Monthly(DateTimeOffset start, DateTimeOffset? end = null, string timezone = "UTC")
    {
        return new TimePartitionDefinition
        {
            Interval = Granularity.Monthly,
            Start = start,
            End = end,
            Timezone = timezone
        };
    }

    /// <summary>
    /// Factory: Create hourly partitions starting from a date.
    /// </summary>
    public static TimePartitionDefinition Hourly(DateTimeOffset start, DateTimeOffset? end = null, string timezone = "UTC")
    {
        return new TimePartitionDefinition
        {
            Interval = Granularity.Hourly,
            Start = start,
            End = end,
            Timezone = timezone
        };
    }

    /// <summary>
    /// Factory: Create quarterly partitions starting from a date.
    /// </summary>
    public static TimePartitionDefinition Quarterly(DateTimeOffset start, DateTimeOffset? end = null, string timezone = "UTC")
    {
        return new TimePartitionDefinition
        {
            Interval = Granularity.Quarterly,
            Start = start,
            End = end,
            Timezone = timezone
        };
    }

    /// <summary>
    /// Factory: Create yearly partitions starting from a date.
    /// </summary>
    public static TimePartitionDefinition Yearly(DateTimeOffset start, DateTimeOffset? end = null, string timezone = "UTC")
    {
        return new TimePartitionDefinition
        {
            Interval = Granularity.Yearly,
            Start = start,
            End = end,
            Timezone = timezone
        };
    }
}
