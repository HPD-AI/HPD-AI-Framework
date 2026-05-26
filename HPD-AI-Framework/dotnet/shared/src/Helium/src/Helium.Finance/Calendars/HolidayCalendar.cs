namespace Helium.Finance.Calendars;

public sealed class HolidayCalendar
{
    private readonly HashSet<DateOnly> _holidays;

    public HolidayCalendar(
        IEnumerable<DateOnly>? holidays = null,
        WeekendRule? weekendRule = null)
    {
        _holidays = holidays is null ? [] : new HashSet<DateOnly>(holidays);
        WeekendRule = weekendRule ?? WeekendRule.SaturdaySunday;
    }

    public static HolidayCalendar WeekendsOnly { get; } = new();

    public WeekendRule WeekendRule { get; }

    public bool IsHoliday(DateOnly date) => _holidays.Contains(date);

    public bool IsWeekend(DateOnly date) => WeekendRule.IsWeekend(date);

    public bool IsBusinessDay(DateOnly date) => !IsWeekend(date) && !IsHoliday(date);

    public bool IsEndOfMonth(DateOnly date) => date >= EndOfMonth(date);

    public DateOnly EndOfMonth(DateOnly date)
    {
        var calendarEnd = new DateOnly(date.Year, date.Month, DateTime.DaysInMonth(date.Year, date.Month));
        return Adjust(calendarEnd, BusinessDayConvention.Preceding);
    }

    public DateOnly Adjust(DateOnly date, BusinessDayConvention convention)
    {
        ValidateConvention(convention);

        if (convention == BusinessDayConvention.Unadjusted || IsBusinessDay(date))
            return date;

        return convention switch
        {
            BusinessDayConvention.Following => NextBusinessDay(date),
            BusinessDayConvention.Preceding => PreviousBusinessDay(date),
            BusinessDayConvention.ModifiedFollowing => ModifiedFollowing(date),
            BusinessDayConvention.ModifiedPreceding => ModifiedPreceding(date),
            _ => throw new ArgumentOutOfRangeException(nameof(convention), convention, "Unsupported business-day convention.")
        };
    }

    private static void ValidateConvention(BusinessDayConvention convention)
    {
        if (convention is not (BusinessDayConvention.Unadjusted
            or BusinessDayConvention.Following
            or BusinessDayConvention.ModifiedFollowing
            or BusinessDayConvention.Preceding
            or BusinessDayConvention.ModifiedPreceding))
        {
            throw new ArgumentOutOfRangeException(nameof(convention), convention, "Unsupported business-day convention.");
        }
    }

    public DateOnly AdvanceBusinessDays(DateOnly date, int businessDays)
    {
        if (businessDays == 0)
            return Adjust(date, BusinessDayConvention.Following);

        if (businessDays == int.MinValue)
            throw new ArgumentOutOfRangeException(nameof(businessDays), businessDays, "Business-day advance is outside the supported integer range.");

        var direction = businessDays > 0 ? 1 : -1;
        var remaining = Math.Abs(businessDays);
        var current = date;

        while (remaining > 0)
        {
            current = AddDaysWithinRange(current, direction);
            if (IsBusinessDay(current))
                remaining--;
        }

        return current;
    }

    public int BusinessDaysBetween(
        DateOnly from,
        DateOnly to,
        bool includeFirst = true,
        bool includeLast = false)
    {
        if (from > to)
            return -BusinessDaysBetween(to, from, includeLast, includeFirst);

        if (from == to)
            return includeFirst && includeLast && IsBusinessDay(from) ? 1 : 0;

        var count = 0;
        for (var current = from; current < to; current = current.AddDays(1))
        {
            if (current == from && !includeFirst)
                continue;

            if (IsBusinessDay(current))
                count++;
        }

        if (includeLast && IsBusinessDay(to))
            count++;

        return count;
    }

    private DateOnly NextBusinessDay(DateOnly date)
    {
        var current = date;
        do
        {
            current = AddDaysWithinRange(current, 1);
        }
        while (!IsBusinessDay(current));

        return current;
    }

    private DateOnly PreviousBusinessDay(DateOnly date)
    {
        var current = date;
        do
        {
            current = AddDaysWithinRange(current, -1);
        }
        while (!IsBusinessDay(current));

        return current;
    }

    private DateOnly ModifiedFollowing(DateOnly date)
    {
        var adjusted = NextBusinessDay(date);
        return adjusted.Month == date.Month ? adjusted : PreviousBusinessDay(date);
    }

    private DateOnly ModifiedPreceding(DateOnly date)
    {
        var adjusted = PreviousBusinessDay(date);
        return adjusted.Month == date.Month ? adjusted : NextBusinessDay(date);
    }

    private static DateOnly AddDaysWithinRange(DateOnly date, int days)
    {
        try
        {
            return date.AddDays(days);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new ArgumentOutOfRangeException(nameof(date), date, "Business-day search exceeded the supported date range.");
        }
    }
}
