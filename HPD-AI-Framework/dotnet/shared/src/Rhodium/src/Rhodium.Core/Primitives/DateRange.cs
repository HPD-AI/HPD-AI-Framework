namespace Rhodium.Primitives;

/// <summary>
/// A bounded time interval. Essential for data queries and backtesting.
/// </summary>
public readonly record struct DateRange(Instant Start, Instant End)
{
    public Duration Duration => End - Start;

    public bool Contains(Instant instant) => instant >= Start && instant <= End;

    public bool Overlaps(DateRange other) => Start <= other.End && End >= other.Start;

    public DateRange? Intersect(DateRange other)
    {
        var start = Instant.Max(Start, other.Start);
        var end = Instant.Min(End, other.End);
        return start <= end ? new DateRange(start, end) : null;
    }

    // Factory methods
    public static DateRange LastDays(int days) =>
        new(Instant.Now - Duration.FromDays(days), Instant.Now);

    public static DateRange LastHours(int hours) =>
        new(Instant.Now - Duration.FromHours(hours), Instant.Now);

    public static DateRange Between(Instant start, Instant end) =>
        new(start, end);

    public static DateRange Year(int year) =>
        new(Instant.FromDateTimeOffset(new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            Instant.FromDateTimeOffset(new DateTimeOffset(year + 1, 1, 1, 0, 0, 0, TimeSpan.Zero)));

    public override string ToString() => $"[{Start} - {End}]";
}
