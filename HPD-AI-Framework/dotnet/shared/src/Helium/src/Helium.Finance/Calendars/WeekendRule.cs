namespace Helium.Finance.Calendars;

public readonly record struct WeekendRule
{
    public WeekendRule(DayOfWeek first, DayOfWeek second)
    {
        ValidateDay(first, nameof(first));
        ValidateDay(second, nameof(second));

        First = first;
        Second = second;
    }

    public static WeekendRule SaturdaySunday { get; } = new(DayOfWeek.Saturday, DayOfWeek.Sunday);

    public DayOfWeek First { get; }

    public DayOfWeek Second { get; }

    public bool IsWeekend(DateOnly date) => date.DayOfWeek == First || date.DayOfWeek == Second;

    private static void ValidateDay(DayOfWeek day, string parameterName)
    {
        if (day is < DayOfWeek.Sunday or > DayOfWeek.Saturday)
            throw new ArgumentOutOfRangeException(parameterName, day, "Weekend day must be a valid day of week.");
    }
}
