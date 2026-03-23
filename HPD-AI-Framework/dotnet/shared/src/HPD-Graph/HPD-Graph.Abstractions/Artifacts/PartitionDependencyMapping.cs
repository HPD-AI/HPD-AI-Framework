using System.Globalization;

namespace HPDAgent.Graph.Abstractions.Artifacts;

/// <summary>
/// Defines cross-partition dependencies for aggregation workflows.
/// Example: Weekly aggregate partition depends on 7 daily input partitions.
/// </summary>
public record PartitionDependencyMapping
{
    /// <summary>
    /// Maps output partition to required input partition keys.
    /// Example: "2025-W03" → ["2025-01-15", "2025-01-16", ..., "2025-01-21"]
    /// </summary>
    public required Func<PartitionKey, IEnumerable<PartitionKey>> MapInputPartitions { get; init; }

    /// <summary>
    /// Factory: Weekly partitions depend on 7 daily partitions.
    /// Maps ISO week format (2025-W03) to 7 daily partitions.
    /// </summary>
    public static PartitionDependencyMapping WeeklyFromDaily()
    {
        return new PartitionDependencyMapping
        {
            MapInputPartitions = weekKey =>
            {
                var weekString = weekKey.Dimensions.FirstOrDefault()
                    ?? throw new ArgumentException("Week partition key must have at least one dimension");

                // Parse ISO week format "2025-W03"
                if (!weekString.Contains("-W"))
                    throw new ArgumentException($"Invalid ISO week format: {weekString}. Expected format: YYYY-WNN");

                var parts = weekString.Split('-');
                if (parts.Length != 2 || !parts[1].StartsWith("W"))
                    throw new ArgumentException($"Invalid ISO week format: {weekString}");

                var year = int.Parse(parts[0]);
                var week = int.Parse(parts[1].Substring(1));

                // Calculate Monday of the given ISO week
                var jan1 = new DateTime(year, 1, 1);
                var daysOffset = DayOfWeek.Monday - jan1.DayOfWeek;
                if (daysOffset > 0) daysOffset -= 7; // Go back to previous Monday if Jan 1 is not Monday

                var firstMonday = jan1.AddDays(daysOffset);

                // ISO week 1 is the first week with at least 4 days in the new year
                var calendar = CultureInfo.InvariantCulture.Calendar;
                var jan4 = new DateTime(year, 1, 4);
                var weekOfJan4 = calendar.GetWeekOfYear(jan4, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);

                var adjustWeeks = weekOfJan4 == 1 ? week - 1 : week;
                var weekStart = firstMonday.AddDays(adjustWeeks * 7);

                // Generate 7 daily partition keys (Monday through Sunday)
                return Enumerable.Range(0, 7)
                    .Select(offset => weekStart.AddDays(offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                    .Select(d => new PartitionKey { Dimensions = new[] { d } });
            }
        };
    }

    /// <summary>
    /// Factory: Monthly partitions depend on daily partitions within that month.
    /// Maps month format (2025-01) to daily partitions.
    /// </summary>
    public static PartitionDependencyMapping MonthlyFromDaily()
    {
        return new PartitionDependencyMapping
        {
            MapInputPartitions = monthKey =>
            {
                var monthString = monthKey.Dimensions.FirstOrDefault()
                    ?? throw new ArgumentException("Month partition key must have at least one dimension");

                // Parse month format "2025-01"
                var parts = monthString.Split('-');
                if (parts.Length != 2)
                    throw new ArgumentException($"Invalid month format: {monthString}. Expected format: YYYY-MM");

                var year = int.Parse(parts[0]);
                var month = int.Parse(parts[1]);

                var daysInMonth = DateTime.DaysInMonth(year, month);

                // Generate daily partition keys for all days in the month
                return Enumerable.Range(1, daysInMonth)
                    .Select(day => new DateTime(year, month, day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                    .Select(d => new PartitionKey { Dimensions = new[] { d } });
            }
        };
    }

    /// <summary>
    /// Factory: Quarterly partitions depend on monthly partitions within that quarter.
    /// Maps quarter format (2025-Q1) to 3 monthly partitions.
    /// </summary>
    public static PartitionDependencyMapping QuarterlyFromMonthly()
    {
        return new PartitionDependencyMapping
        {
            MapInputPartitions = quarterKey =>
            {
                var quarterString = quarterKey.Dimensions.FirstOrDefault()
                    ?? throw new ArgumentException("Quarter partition key must have at least one dimension");

                // Parse quarter format "2025-Q1"
                if (!quarterString.Contains("-Q"))
                    throw new ArgumentException($"Invalid quarter format: {quarterString}. Expected format: YYYY-QN");

                var parts = quarterString.Split('-');
                if (parts.Length != 2 || !parts[1].StartsWith("Q"))
                    throw new ArgumentException($"Invalid quarter format: {quarterString}");

                var year = int.Parse(parts[0]);
                var quarter = int.Parse(parts[1].Substring(1));

                if (quarter < 1 || quarter > 4)
                    throw new ArgumentException($"Invalid quarter: {quarter}. Must be 1-4");

                // Q1 = Jan-Mar (months 1-3), Q2 = Apr-Jun (months 4-6), etc.
                var startMonth = (quarter - 1) * 3 + 1;

                // Generate 3 monthly partition keys
                return Enumerable.Range(0, 3)
                    .Select(offset => new DateTime(year, startMonth + offset, 1).ToString("yyyy-MM", CultureInfo.InvariantCulture))
                    .Select(m => new PartitionKey { Dimensions = new[] { m } });
            }
        };
    }

    /// <summary>
    /// Factory: Yearly partitions depend on monthly partitions within that year.
    /// Maps year format (2025) to 12 monthly partitions.
    /// </summary>
    public static PartitionDependencyMapping YearlyFromMonthly()
    {
        return new PartitionDependencyMapping
        {
            MapInputPartitions = yearKey =>
            {
                var yearString = yearKey.Dimensions.FirstOrDefault()
                    ?? throw new ArgumentException("Year partition key must have at least one dimension");

                var year = int.Parse(yearString);

                // Generate 12 monthly partition keys
                return Enumerable.Range(1, 12)
                    .Select(month => new DateTime(year, month, 1).ToString("yyyy-MM", CultureInfo.InvariantCulture))
                    .Select(m => new PartitionKey { Dimensions = new[] { m } });
            }
        };
    }

    /// <summary>
    /// Factory: Custom mapping function.
    /// </summary>
    public static PartitionDependencyMapping Custom(Func<PartitionKey, IEnumerable<PartitionKey>> mapper)
    {
        return new PartitionDependencyMapping { MapInputPartitions = mapper };
    }
}
