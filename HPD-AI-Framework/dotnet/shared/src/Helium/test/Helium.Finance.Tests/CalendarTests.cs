using Helium.Finance.Calendars;

namespace Helium.Finance.Tests;

public class CalendarTests
{
    [Fact]
    public void WeekendsOnlyCalendarRecognizesBusinessDays()
    {
        var calendar = HolidayCalendar.WeekendsOnly;

        Assert.True(calendar.IsBusinessDay(new DateOnly(2026, 5, 22)));
        Assert.False(calendar.IsBusinessDay(new DateOnly(2026, 5, 23)));
        Assert.False(calendar.IsBusinessDay(new DateOnly(2026, 5, 24)));
    }

    [Fact]
    public void CalendarRecognizesExplicitHoliday()
    {
        var calendar = new HolidayCalendar([new DateOnly(2026, 1, 1)]);

        Assert.False(calendar.IsBusinessDay(new DateOnly(2026, 1, 1)));
        Assert.True(calendar.IsHoliday(new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void CalendarSnapshotsHolidayCollection()
    {
        var holidays = new List<DateOnly>
        {
            new(2026, 1, 1)
        };
        var calendar = new HolidayCalendar(holidays);
        holidays.Clear();

        Assert.True(calendar.IsHoliday(new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void WeekendRuleRejectsInvalidDays()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WeekendRule((DayOfWeek)(-1), DayOfWeek.Sunday));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WeekendRule(DayOfWeek.Saturday, (DayOfWeek)7));
    }

    [Fact]
    public void CalendarUsesCustomWeekendRule()
    {
        var calendar = new HolidayCalendar(weekendRule: new WeekendRule(DayOfWeek.Friday, DayOfWeek.Saturday));

        Assert.False(calendar.IsBusinessDay(new DateOnly(2026, 5, 22)));
        Assert.False(calendar.IsBusinessDay(new DateOnly(2026, 5, 23)));
        Assert.True(calendar.IsBusinessDay(new DateOnly(2026, 5, 24)));
    }

    [Fact]
    public void FollowingAdjustsToNextBusinessDay()
    {
        var calendar = HolidayCalendar.WeekendsOnly;

        var adjusted = calendar.Adjust(new DateOnly(2026, 5, 23), BusinessDayConvention.Following);

        Assert.Equal(new DateOnly(2026, 5, 25), adjusted);
    }

    [Fact]
    public void AdjustRejectsInvalidBusinessDayConventionEvenOnBusinessDay()
    {
        var calendar = HolidayCalendar.WeekendsOnly;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            calendar.Adjust(new DateOnly(2026, 5, 22), (BusinessDayConvention)999));
    }

    [Fact]
    public void PrecedingAdjustsToPreviousBusinessDay()
    {
        var calendar = HolidayCalendar.WeekendsOnly;

        var adjusted = calendar.Adjust(new DateOnly(2026, 5, 24), BusinessDayConvention.Preceding);

        Assert.Equal(new DateOnly(2026, 5, 22), adjusted);
    }

    [Fact]
    public void ModifiedFollowingDoesNotCrossMonth()
    {
        var calendar = HolidayCalendar.WeekendsOnly;

        var adjusted = calendar.Adjust(new DateOnly(2026, 1, 31), BusinessDayConvention.ModifiedFollowing);

        Assert.Equal(new DateOnly(2026, 1, 30), adjusted);
    }

    [Fact]
    public void AdvancesBusinessDaysSkippingWeekendAndHoliday()
    {
        var calendar = new HolidayCalendar([new DateOnly(2026, 5, 25)]);

        var advanced = calendar.AdvanceBusinessDays(new DateOnly(2026, 5, 22), 1);

        Assert.Equal(new DateOnly(2026, 5, 26), advanced);
    }

    [Fact]
    public void BusinessDaysBetweenIncludesFirstAndExcludesLastByDefault()
    {
        var calendar = HolidayCalendar.WeekendsOnly;

        Assert.Equal(1, calendar.BusinessDaysBetween(
            new DateOnly(2026, 1, 2),
            new DateOnly(2026, 1, 5)));
        Assert.Equal(2, calendar.BusinessDaysBetween(
            new DateOnly(2026, 1, 2),
            new DateOnly(2026, 1, 5),
            includeLast: true));
    }

    [Fact]
    public void BusinessDaysBetweenHandlesSameDayAndReversedRanges()
    {
        var calendar = HolidayCalendar.WeekendsOnly;

        Assert.Equal(0, calendar.BusinessDaysBetween(
            new DateOnly(2026, 1, 2),
            new DateOnly(2026, 1, 2)));
        Assert.Equal(1, calendar.BusinessDaysBetween(
            new DateOnly(2026, 1, 2),
            new DateOnly(2026, 1, 2),
            includeLast: true));
        Assert.Equal(-1, calendar.BusinessDaysBetween(
            new DateOnly(2026, 1, 5),
            new DateOnly(2026, 1, 2)));
    }

    [Fact]
    public void BusinessDaysBetweenDoesNotStepPastMaximumDate()
    {
        var calendar = HolidayCalendar.WeekendsOnly;
        var from = DateOnly.MaxValue.AddDays(-1);
        var to = DateOnly.MaxValue;
        var expected = 0;
        if (calendar.IsBusinessDay(from))
            expected++;
        if (calendar.IsBusinessDay(to))
            expected++;

        var actual = calendar.BusinessDaysBetween(from, to, includeLast: true);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BusinessDaySearchRejectsDateRangeOverflow()
    {
        var calendar = new HolidayCalendar([DateOnly.MinValue, DateOnly.MaxValue]);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            calendar.Adjust(DateOnly.MaxValue, BusinessDayConvention.Following));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            calendar.Adjust(DateOnly.MinValue, BusinessDayConvention.Preceding));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            calendar.AdvanceBusinessDays(DateOnly.MaxValue, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            calendar.AdvanceBusinessDays(DateOnly.MinValue, -1));
    }

    [Fact]
    public void BusinessDayAdvanceRejectsMinimumIntegerDistanceAsRangeError()
    {
        var calendar = HolidayCalendar.WeekendsOnly;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            calendar.AdvanceBusinessDays(new DateOnly(2026, 1, 2), int.MinValue));
    }
}
