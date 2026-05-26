using Helium.Finance.Conventions;
using Helium.Finance.Calendars;
using Helium.Primitives;
using System.Numerics;

namespace Helium.Finance.Tests;

public class YearFractionTests
{
    [Fact]
    public void ConvertsExactRationalFixtureThroughNamedFinanceConversion()
    {
        var exact = Rational.Create((Integer)1, (Integer)4);
        var yearFraction = YearFraction.FromRational(exact);

        Assert.Equal(0.25, yearFraction.Value);
    }

    [Fact]
    public void FinanceRationalConversionRejectsNonfiniteDoubleResult()
    {
        var huge = Rational.Create((Integer)BigInteger.Pow(new BigInteger(10), 400), (Integer)1);

        Assert.Throws<ArgumentOutOfRangeException>(() => FinanceConvert.ToDouble(huge));
        Assert.Throws<ArgumentOutOfRangeException>(() => YearFraction.FromRational(huge));
    }

    [Fact]
    public void Thirty360UsAppliesFebruaryEndOfMonthRules()
    {
        var yearFraction = DayCounts.YearFraction(
            new DateOnly(2024, 2, 29),
            new DateOnly(2024, 3, 31),
            DayCountConvention.Thirty360Us);

        Assert.Equal(30, DayCounts.DayCount(
            new DateOnly(2024, 2, 29),
            new DateOnly(2024, 3, 31),
            DayCountConvention.Thirty360Us));
        Assert.Equal(30.0 / 360.0, yearFraction.Value);
    }

    [Fact]
    public void Business252UsesCalendarBusinessDays()
    {
        var calendar = new HolidayCalendar([new DateOnly(2026, 1, 5)]);
        var options = new DayCountOptions(Calendar: calendar);

        Assert.Equal(2, DayCounts.DayCount(
            new DateOnly(2026, 1, 2),
            new DateOnly(2026, 1, 7),
            DayCountConvention.Business252,
            options));
        Assert.Equal(2.0 / 252.0, DayCounts.YearFraction(
            new DateOnly(2026, 1, 2),
            new DateOnly(2026, 1, 7),
            DayCountConvention.Business252,
            options).Value);
    }

    [Fact]
    public void Business252PreservesSignForReversedDates()
    {
        Assert.Equal(-1, DayCounts.DayCount(
            new DateOnly(2026, 1, 5),
            new DateOnly(2026, 1, 2),
            DayCountConvention.Business252));
        Assert.Equal(-1.0 / 252.0, DayCounts.YearFraction(
            new DateOnly(2026, 1, 5),
            new DateOnly(2026, 1, 2),
            DayCountConvention.Business252).Value);
    }

    [Fact]
    public void Thirty360BondBasisKeepsUsFebruaryRuleOutOfBondBasis()
    {
        Assert.Equal(33, DayCounts.DayCount(
            new DateOnly(2024, 2, 28),
            new DateOnly(2024, 3, 31),
            DayCountConvention.Thirty360BondBasis));
    }

    [Fact]
    public void ThirtyE360IsdaPreservesTerminationDateLastFebruary()
    {
        var start = new DateOnly(2023, 8, 31);
        var termination = new DateOnly(2024, 2, 29);

        Assert.Equal(180, DayCounts.DayCount(start, termination, DayCountConvention.ThirtyE360Isda));
        Assert.Equal(179, DayCounts.DayCount(
            start,
            termination,
            DayCountConvention.ThirtyE360Isda,
            new DayCountOptions(TerminationDate: termination)));
    }

    [Fact]
    public void Thirty360ItalianTreatsLateFebruaryAsThirty()
    {
        Assert.Equal(30, DayCounts.DayCount(
            new DateOnly(2024, 2, 28),
            new DateOnly(2024, 3, 31),
            DayCountConvention.Thirty360Italian));
    }

    [Fact]
    public void Thirty360NasdRollsMonthWhenEndDateIsThirtyFirstAfterShortStart()
    {
        Assert.Equal(16, DayCounts.DayCount(
            new DateOnly(2026, 1, 15),
            new DateOnly(2026, 1, 31),
            DayCountConvention.Thirty360Nasd));
    }

    [Fact]
    public void ActualActualAfbCountsWholeYearsAndLeapPartialYears()
    {
        Assert.Equal(1.0, DayCounts.YearFraction(
            new DateOnly(2023, 2, 28),
            new DateOnly(2024, 2, 29),
            DayCountConvention.ActualActualAfb).Value);

        Assert.Equal(2.0 / 366.0, DayCounts.YearFraction(
            new DateOnly(2024, 2, 28),
            new DateOnly(2024, 3, 1),
            DayCountConvention.ActualActualAfb).Value);
    }

    [Fact]
    public void DayCountAndYearFractionPreserveSignForReversedDates()
    {
        Assert.Equal(-30, DayCounts.DayCount(
            new DateOnly(2024, 3, 31),
            new DateOnly(2024, 2, 29),
            DayCountConvention.Thirty360Us));

        Assert.Equal(-30.0 / 360.0, DayCounts.YearFraction(
            new DateOnly(2024, 3, 31),
            new DateOnly(2024, 2, 29),
            DayCountConvention.Thirty360Us).Value);
    }
}
