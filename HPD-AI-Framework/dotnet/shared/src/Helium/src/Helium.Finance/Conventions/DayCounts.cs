using Helium.Finance.Calendars;

namespace Helium.Finance.Conventions;

public static class DayCounts
{
    public static YearFraction YearFraction(
        DateOnly start,
        DateOnly end,
        DayCountConvention convention,
        DayCountOptions options = default)
    {
        var sign = start <= end ? 1.0 : -1.0;
        if (start > end)
            (start, end) = (end, start);

        var value = convention switch
        {
            DayCountConvention.Actual360 => sign * DayCount(start, end, convention, options) / 360.0,
            DayCountConvention.Actual365Fixed => sign * DayCount(start, end, convention, options) / 365.0,
            DayCountConvention.Business252 => sign * DayCount(start, end, convention, options) / 252.0,
            DayCountConvention.ActualActualIsda => sign * ActualActualIsda(start, end),
            DayCountConvention.ActualActualAfb => sign * ActualActualAfb(start, end),
            DayCountConvention.Thirty360Us
                or DayCountConvention.Thirty360BondBasis
                or DayCountConvention.ThirtyE360
                or DayCountConvention.ThirtyE360Isda
                or DayCountConvention.Thirty360Italian
                or DayCountConvention.Thirty360Nasd => sign * DayCount(start, end, convention, options) / 360.0,
            _ => throw new ArgumentOutOfRangeException(nameof(convention), convention, "Unsupported day count convention.")
        };

        return new YearFraction(value);
    }

    public static int DayCount(
        DateOnly start,
        DateOnly end,
        DayCountConvention convention,
        DayCountOptions options = default)
    {
        var sign = start <= end ? 1 : -1;
        if (start > end)
            (start, end) = (end, start);

        var value = convention switch
        {
            DayCountConvention.Actual360
                or DayCountConvention.Actual365Fixed
                or DayCountConvention.ActualActualIsda
                or DayCountConvention.ActualActualAfb => end.DayNumber - start.DayNumber,
            DayCountConvention.Business252 => BusinessDaysBetween(start, end, options.Calendar),
            DayCountConvention.Thirty360Us => Days360Us(start, end),
            DayCountConvention.Thirty360BondBasis => Days360BondBasis(start, end),
            DayCountConvention.ThirtyE360 => DaysThirtyE360(start, end),
            DayCountConvention.ThirtyE360Isda => DaysThirtyE360Isda(start, end, options.TerminationDate),
            DayCountConvention.Thirty360Italian => Days360Italian(start, end),
            DayCountConvention.Thirty360Nasd => Days360Nasd(start, end),
            _ => throw new ArgumentOutOfRangeException(nameof(convention), convention, "Unsupported day count convention.")
        };

        return sign * value;
    }

    private static int Days360Us(DateOnly start, DateOnly end)
    {
        var d1 = start.Day;
        var d2 = end.Day;

        if (IsLastOfFebruary(start))
        {
            if (IsLastOfFebruary(end))
                d2 = 30;

            d1 = 30;
        }

        if (d2 == 31 && d1 >= 30)
            d2 = 30;

        if (d1 == 31)
            d1 = 30;

        return Days360(start, end, d1, d2, end.Month, end.Year);
    }

    private static int BusinessDaysBetween(DateOnly start, DateOnly end, HolidayCalendar? calendar)
    {
        return (calendar ?? HolidayCalendar.WeekendsOnly).BusinessDaysBetween(start, end);
    }

    private static int Days360BondBasis(DateOnly start, DateOnly end)
    {
        var d1 = start.Day == 31 ? 30 : start.Day;
        var d2 = end.Day == 31 && d1 == 30 ? 30 : end.Day;

        return Days360(start, end, d1, d2, end.Month, end.Year);
    }

    private static int DaysThirtyE360(DateOnly start, DateOnly end)
    {
        var d1 = Math.Min(start.Day, 30);
        var d2 = Math.Min(end.Day, 30);

        return Days360(start, end, d1, d2, end.Month, end.Year);
    }

    private static int DaysThirtyE360Isda(DateOnly start, DateOnly end, DateOnly? terminationDate)
    {
        var d1 = start.Day;
        var d2 = end.Day;

        if (d1 == 31 || IsLastOfFebruary(start))
            d1 = 30;

        if (d2 == 31 || (end != terminationDate && IsLastOfFebruary(end)))
            d2 = 30;

        return Days360(start, end, d1, d2, end.Month, end.Year);
    }

    private static int Days360Italian(DateOnly start, DateOnly end)
    {
        var d1 = start.Day;
        var d2 = end.Day;

        if (d1 == 31 || (start.Month == 2 && d1 > 27))
            d1 = 30;

        if (d2 == 31 || (end.Month == 2 && d2 > 27))
            d2 = 30;

        return Days360(start, end, d1, d2, end.Month, end.Year);
    }

    private static int Days360Nasd(DateOnly start, DateOnly end)
    {
        var d1 = start.Day;
        var d2 = end.Day;
        var m2 = end.Month;
        var y2 = end.Year;

        if (d1 == 31)
            d1 = 30;

        if (d2 == 31 && d1 >= 30)
        {
            d2 = 30;
        }
        else if (d2 == 31)
        {
            d2 = 1;
            m2++;
            if (m2 == 13)
            {
                m2 = 1;
                y2++;
            }
        }

        return Days360(start, end, d1, d2, m2, y2);
    }

    private static int Days360(DateOnly start, DateOnly end, int d1, int d2, int endMonth, int endYear)
    {
        return 360 * (endYear - start.Year)
            + 30 * (endMonth - start.Month)
            + d2 - d1;
    }

    private static double ActualActualIsda(DateOnly start, DateOnly end)
    {
        if (start == end)
            return 0.0;

        var total = 0.0;
        var current = start;
        while (current < end)
        {
            var nextYear = new DateOnly(current.Year + 1, 1, 1);
            var segmentEnd = nextYear < end ? nextYear : end;
            var denominator = DateTime.IsLeapYear(current.Year) ? 366.0 : 365.0;
            total += (segmentEnd.DayNumber - current.DayNumber) / denominator;
            current = segmentEnd;
        }

        return total;
    }

    private static double ActualActualAfb(DateOnly start, DateOnly end)
    {
        if (start == end)
            return 0.0;

        var newEnd = end;
        var probe = end;
        var wholeYears = 0.0;
        while (probe > start)
        {
            probe = newEnd.AddYears(-1);
            if (probe.Day == 28 && probe.Month == 2 && DateTime.IsLeapYear(probe.Year))
                probe = probe.AddDays(1);

            if (probe < start)
                continue;

            wholeYears += 1.0;
            newEnd = probe;
        }

        var denominator = 365.0;
        if (DateTime.IsLeapYear(newEnd.Year))
        {
            var leapDay = new DateOnly(newEnd.Year, 2, 29);
            if (newEnd > leapDay && start <= leapDay)
                denominator = 366.0;
        }
        else if (DateTime.IsLeapYear(start.Year))
        {
            var leapDay = new DateOnly(start.Year, 2, 29);
            if (newEnd > leapDay && start <= leapDay)
                denominator = 366.0;
        }

        return wholeYears + (newEnd.DayNumber - start.DayNumber) / denominator;
    }

    private static bool IsLastOfFebruary(DateOnly date)
    {
        return date.Month == 2 && date.Day == DateTime.DaysInMonth(date.Year, 2);
    }
}
