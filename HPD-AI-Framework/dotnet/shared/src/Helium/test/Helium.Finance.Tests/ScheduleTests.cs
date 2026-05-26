using Helium.Finance.Calendars;
using Helium.Finance.Schedules;

namespace Helium.Finance.Tests;

public class ScheduleTests
{
    [Fact]
    public void GeneratesZeroScheduleWithAdjustedBoundaries()
    {
        var input = new ScheduleInput(
            new DateOnly(2026, 5, 23),
            new DateOnly(2026, 8, 23),
            ScheduleTenor.Zero,
            HolidayCalendar.WeekendsOnly,
            BusinessDayConvention.Following,
            BusinessDayConvention.Preceding,
            DateGenerationRule.Zero);

        var schedule = ScheduleGenerator.Generate(input);

        Assert.Equal(
            [new DateOnly(2026, 5, 25), new DateOnly(2026, 8, 21)],
            schedule.Dates);
        Assert.Equal([true], schedule.IsRegular);
    }

    [Fact]
    public void GeneratesForwardMonthlySchedule()
    {
        var input = ScheduleInput.Forward(
            new DateOnly(2026, 1, 15),
            new DateOnly(2026, 7, 15),
            ScheduleTenor.Monthly,
            convention: BusinessDayConvention.Unadjusted);

        var schedule = ScheduleGenerator.Generate(input);

        Assert.Equal(
            [
                new DateOnly(2026, 1, 15),
                new DateOnly(2026, 2, 15),
                new DateOnly(2026, 3, 15),
                new DateOnly(2026, 4, 15),
                new DateOnly(2026, 5, 15),
                new DateOnly(2026, 6, 15),
                new DateOnly(2026, 7, 15)
            ],
            schedule.Dates);
        Assert.All(schedule.IsRegular, Assert.True);
    }

    [Fact]
    public void DateScheduleRejectsNullInputs()
    {
        var input = ScheduleInput.Forward(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 2, 1),
            ScheduleTenor.Monthly,
            convention: BusinessDayConvention.Unadjusted);

        Assert.Throws<ArgumentNullException>(() => new DateSchedule(null!, [true], input));
        Assert.Throws<ArgumentNullException>(() => new DateSchedule([input.EffectiveDate, input.TerminationDate], null!, input));
    }

    [Fact]
    public void DateScheduleRejectsMalformedDatesAndRegularity()
    {
        var input = ScheduleInput.Forward(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 3, 1),
            ScheduleTenor.Monthly,
            convention: BusinessDayConvention.Unadjusted);

        Assert.Throws<ArgumentException>(() => new DateSchedule([input.EffectiveDate], [], input));
        Assert.Throws<ArgumentException>(() => new DateSchedule([input.EffectiveDate, input.TerminationDate], [], input));
        Assert.Throws<ArgumentException>(() => new DateSchedule(
            [input.TerminationDate, input.EffectiveDate],
            [true],
            input));
        Assert.Throws<ArgumentException>(() => new DateSchedule(
            [input.EffectiveDate, input.EffectiveDate],
            [true],
            input));
    }

    [Fact]
    public void DateScheduleSnapshotsInputCollections()
    {
        var input = ScheduleInput.Forward(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 2, 1),
            ScheduleTenor.Monthly,
            convention: BusinessDayConvention.Unadjusted);
        var dates = new List<DateOnly> { input.EffectiveDate, input.TerminationDate };
        var regularity = new List<bool> { true };

        var schedule = new DateSchedule(dates, regularity, input);
        dates[0] = new DateOnly(2030, 1, 1);
        regularity[0] = false;

        Assert.Equal(input.EffectiveDate, schedule.StartDate);
        Assert.True(schedule.IsRegular[0]);
    }

    [Fact]
    public void GeneratesBackwardQuarterlySchedule()
    {
        var input = ScheduleInput.Backward(
            new DateOnly(2026, 1, 15),
            new DateOnly(2026, 10, 15),
            ScheduleTenor.Quarterly,
            convention: BusinessDayConvention.Unadjusted);

        var schedule = ScheduleGenerator.Generate(input);

        Assert.Equal(
            [
                new DateOnly(2026, 1, 15),
                new DateOnly(2026, 4, 15),
                new DateOnly(2026, 7, 15),
                new DateOnly(2026, 10, 15)
            ],
            schedule.Dates);
        Assert.All(schedule.IsRegular, Assert.True);
    }

    [Fact]
    public void MarksExplicitForwardStubAsIrregular()
    {
        var input = ScheduleInput.Forward(
            new DateOnly(2026, 1, 15),
            new DateOnly(2026, 7, 15),
            ScheduleTenor.Quarterly,
            convention: BusinessDayConvention.Unadjusted,
            firstDate: new DateOnly(2026, 2, 15));

        var schedule = ScheduleGenerator.Generate(input);

        Assert.Equal(
            [
                new DateOnly(2026, 1, 15),
                new DateOnly(2026, 2, 15),
                new DateOnly(2026, 5, 15),
                new DateOnly(2026, 7, 15)
            ],
            schedule.Dates);
        Assert.Equal([false, true, false], schedule.IsRegular);
    }

    [Fact]
    public void EndOfMonthScheduleKeepsIntermediateDatesAtBusinessMonthEnd()
    {
        var input = ScheduleInput.Forward(
            new DateOnly(2026, 1, 31),
            new DateOnly(2026, 5, 31),
            ScheduleTenor.Monthly,
            convention: BusinessDayConvention.Unadjusted,
            endOfMonth: true);

        var schedule = ScheduleGenerator.Generate(input);

        Assert.Equal(
            [
                new DateOnly(2026, 1, 31),
                new DateOnly(2026, 2, 27),
                new DateOnly(2026, 3, 31),
                new DateOnly(2026, 4, 30),
                new DateOnly(2026, 5, 31)
            ],
            schedule.Dates);
    }

    [Fact]
    public void RemovesDuplicateDatesCreatedByBusinessDayAdjustment()
    {
        var input = ScheduleInput.Forward(
            new DateOnly(2026, 5, 22),
            new DateOnly(2026, 5, 24),
            ScheduleTenor.Days(1),
            convention: BusinessDayConvention.Following);

        var schedule = ScheduleGenerator.Generate(input);

        Assert.Equal([new DateOnly(2026, 5, 22), new DateOnly(2026, 5, 25)], schedule.Dates);
        Assert.Equal([true], schedule.IsRegular);
    }

    [Fact]
    public void ScheduleTenorRejectsInvalidConstructionAndMutation()
    {
        var tenor = ScheduleTenor.Monthly;

        Assert.Throws<ArgumentOutOfRangeException>(() => new ScheduleTenor(-1, TenorUnit.Months));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScheduleTenor(1, (TenorUnit)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => tenor with { Length = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => tenor with { Unit = (TenorUnit)999 });
    }

    [Fact]
    public void ScheduleTenorRejectsDateArithmeticOverflowAsRangeError()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ScheduleTenor(int.MaxValue, TenorUnit.Weeks).AddTo(new DateOnly(2026, 1, 1), 2, endOfMonth: false));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ScheduleTenor.Monthly.AddTo(DateOnly.MaxValue, 1, endOfMonth: false));
    }

    [Fact]
    public void ScheduleGenerationRejectsInvalidInputPoliciesBeforeEvaluation()
    {
        var valid = ScheduleInput.Forward(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 2, 1),
            ScheduleTenor.Monthly,
            convention: BusinessDayConvention.Unadjusted);

        Assert.Throws<ArgumentNullException>(() => ScheduleGenerator.Generate(valid with { Calendar = null! }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScheduleGenerator.Generate(valid with { Convention = (BusinessDayConvention)999 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScheduleGenerator.Generate(valid with { TerminationDateConvention = (BusinessDayConvention)999 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ScheduleGenerator.Generate(valid with { Rule = (DateGenerationRule)999 }));
    }

    [Fact]
    public void ExposesPreviousAndNextDatesWithoutGlobalEvaluationState()
    {
        var schedule = ScheduleGenerator.Generate(ScheduleInput.Forward(
            new DateOnly(2026, 1, 15),
            new DateOnly(2026, 7, 15),
            ScheduleTenor.Quarterly,
            convention: BusinessDayConvention.Unadjusted));

        Assert.Equal(new DateOnly(2026, 4, 15), schedule.PreviousDate(new DateOnly(2026, 5, 1)));
        Assert.Equal(new DateOnly(2026, 7, 15), schedule.NextDate(new DateOnly(2026, 5, 1)));
        Assert.Null(schedule.PreviousDate(new DateOnly(2026, 1, 15)));
        Assert.Null(schedule.NextDate(new DateOnly(2026, 7, 15)));
    }
}
