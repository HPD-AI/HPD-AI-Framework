namespace Rhodium.Primitives;

/// <summary>
/// Bar aggregation period. Wraps Duration with convenient factories.
/// </summary>
public readonly record struct BarPeriod(Duration Duration)
{
    // Factory methods
    public static BarPeriod Seconds(int s) => new(Duration.FromSeconds(s));
    public static BarPeriod Minutes(int m) => new(Duration.FromMinutes(m));
    public static BarPeriod Hours(int h) => new(Duration.FromHours(h));
    public static BarPeriod Days(int d) => new(Duration.FromDays(d));

    // Common periods
    public static BarPeriod M1 => Minutes(1);
    public static BarPeriod M5 => Minutes(5);
    public static BarPeriod M15 => Minutes(15);
    public static BarPeriod M30 => Minutes(30);
    public static BarPeriod H1 => Hours(1);
    public static BarPeriod H4 => Hours(4);
    public static BarPeriod D1 => Days(1);

    public override string ToString() => Duration.TotalMinutes switch
    {
        < 1 => $"{Duration.TotalSeconds:F0}s",
        < 60 => $"{Duration.TotalMinutes:F0}m",
        < 1440 => $"{Duration.TotalHours:F0}h",
        _ => $"{Duration.TotalDays:F0}d"
    };
}
