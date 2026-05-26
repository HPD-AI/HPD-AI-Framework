using Helium.Finance.Calendars;

namespace Helium.Finance.Schedules;

public static class ScheduleGenerator
{
    public static DateSchedule Generate(ScheduleInput input)
    {
        Validate(input);

        var endOfMonth = input.EndOfMonth && input.Tenor.AllowsEndOfMonth;
        var dates = new List<DateOnly>();
        var isRegular = new List<bool>();

        if (input.Rule == DateGenerationRule.Zero || input.Tenor.Length == 0)
        {
            dates.Add(input.EffectiveDate);
            dates.Add(input.TerminationDate);
            isRegular.Add(true);
            AdjustDates(input, dates, endOfMonth: false);
            return new DateSchedule(dates, isRegular, input);
        }

        switch (input.Rule)
        {
            case DateGenerationRule.Forward:
                GenerateForward(input, endOfMonth, dates, isRegular);
                break;
            case DateGenerationRule.Backward:
                GenerateBackward(input, endOfMonth, dates, isRegular);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(input), input.Rule, "Unsupported date generation rule.");
        }

        AdjustDates(input, dates, endOfMonth);
        RemovePostAdjustmentDuplicates(dates, isRegular);

        if (dates.Count < 2)
            throw new InvalidOperationException("Schedule generation produced fewer than two dates.");

        return new DateSchedule(dates, isRegular, input);
    }

    private static void GenerateForward(
        ScheduleInput input,
        bool endOfMonth,
        List<DateOnly> dates,
        List<bool> isRegular)
    {
        dates.Add(input.EffectiveDate);
        var seed = input.EffectiveDate;

        if (input.FirstDate is { } firstDate)
        {
            dates.Add(firstDate);
            isRegular.Add(input.Tenor.AddTo(seed, 1, endOfMonth) == firstDate);
            seed = firstDate;
        }

        var exitDate = input.NextToLastDate ?? input.TerminationDate;
        var periods = 1;
        while (true)
        {
            var candidate = input.Tenor.AddTo(seed, periods, endOfMonth);
            if (candidate > exitDate)
            {
                if (input.NextToLastDate is { } nextToLastDate &&
                    !AdjustEqual(input.Calendar, input.Convention, dates[^1], nextToLastDate))
                {
                    dates.Add(nextToLastDate);
                    isRegular.Add(input.Tenor.AddTo(dates[^2], 1, endOfMonth) == nextToLastDate);
                }

                break;
            }

            if (!AdjustEqual(input.Calendar, input.Convention, dates[^1], candidate))
            {
                dates.Add(candidate);
                isRegular.Add(true);
            }

            periods++;
        }

        if (!AdjustEqual(input.Calendar, input.TerminationDateConvention, dates[^1], input.TerminationDate))
        {
            dates.Add(input.TerminationDate);
            isRegular.Add(false);
        }
    }

    private static void GenerateBackward(
        ScheduleInput input,
        bool endOfMonth,
        List<DateOnly> dates,
        List<bool> isRegular)
    {
        dates.Add(input.TerminationDate);
        var seed = input.TerminationDate;

        if (input.NextToLastDate is { } nextToLastDate)
        {
            dates.Add(nextToLastDate);
            isRegular.Add(input.Tenor.AddTo(seed, -1, endOfMonth) == nextToLastDate);
            seed = nextToLastDate;
        }

        var exitDate = input.FirstDate ?? input.EffectiveDate;
        var periods = 1;
        while (true)
        {
            var candidate = input.Tenor.AddTo(seed, -periods, endOfMonth);
            if (candidate < exitDate)
            {
                if (input.FirstDate is { } firstDate &&
                    !AdjustEqual(input.Calendar, input.Convention, dates[^1], firstDate))
                {
                    dates.Add(firstDate);
                    isRegular.Add(input.Tenor.AddTo(dates[^2], -1, endOfMonth) == firstDate);
                }

                break;
            }

            if (!AdjustEqual(input.Calendar, input.Convention, dates[^1], candidate))
            {
                dates.Add(candidate);
                isRegular.Add(true);
            }

            periods++;
        }

        if (!AdjustEqual(input.Calendar, input.Convention, dates[^1], input.EffectiveDate))
        {
            dates.Add(input.EffectiveDate);
            isRegular.Add(input.Tenor.AddTo(dates[^2], -1, endOfMonth) == input.EffectiveDate);
        }

        dates.Reverse();
        isRegular.Reverse();
    }

    private static void AdjustDates(ScheduleInput input, List<DateOnly> dates, bool endOfMonth)
    {
        if (input.Convention != BusinessDayConvention.Unadjusted)
            dates[0] = input.Calendar.Adjust(dates[0], input.Convention);

        if (input.TerminationDateConvention != BusinessDayConvention.Unadjusted)
            dates[^1] = input.Calendar.Adjust(dates[^1], input.TerminationDateConvention);

        var shouldUseEndOfMonth = endOfMonth && input.Calendar.IsEndOfMonth(input.Rule == DateGenerationRule.Backward ? input.TerminationDate : input.EffectiveDate);
        for (var i = 1; i < dates.Count - 1; i++)
        {
            dates[i] = shouldUseEndOfMonth
                ? input.Calendar.EndOfMonth(dates[i])
                : input.Calendar.Adjust(dates[i], input.Convention);
        }
    }

    private static void RemovePostAdjustmentDuplicates(List<DateOnly> dates, List<bool> isRegular)
    {
        for (var i = dates.Count - 1; i > 0; i--)
        {
            if (dates[i] != dates[i - 1])
                continue;

            dates.RemoveAt(i);
            isRegular.RemoveAt(i - 1);
        }
    }

    private static bool AdjustEqual(
        HolidayCalendar calendar,
        BusinessDayConvention convention,
        DateOnly left,
        DateOnly right)
    {
        return calendar.Adjust(left, convention) == calendar.Adjust(right, convention);
    }

    private static void Validate(ScheduleInput input)
    {
        ArgumentNullException.ThrowIfNull(input.Calendar);

        input.Tenor.Validate();

        if (input.EffectiveDate >= input.TerminationDate)
            throw new ArgumentOutOfRangeException(nameof(input), "Effective date must be before termination date.");

        ValidateBusinessDayConvention(input.Convention, nameof(input.Convention));
        ValidateBusinessDayConvention(input.TerminationDateConvention, nameof(input.TerminationDateConvention));
        ValidateDateGenerationRule(input.Rule);

        if (input.Tenor.Length == 0 && input.Rule != DateGenerationRule.Zero)
            throw new ArgumentOutOfRangeException(nameof(input), "A zero tenor requires zero date generation.");

        if (input.FirstDate is { } firstDate &&
            (firstDate <= input.EffectiveDate || firstDate > input.TerminationDate))
            throw new ArgumentOutOfRangeException(nameof(input), "First date must be inside the effective-termination range.");

        if (input.NextToLastDate is { } nextToLastDate &&
            (nextToLastDate < input.EffectiveDate || nextToLastDate >= input.TerminationDate))
            throw new ArgumentOutOfRangeException(nameof(input), "Next-to-last date must be inside the effective-termination range.");

        if (input.FirstDate is { } first && input.NextToLastDate is { } nextToLast && first > nextToLast)
            throw new ArgumentOutOfRangeException(nameof(input), "First date cannot be after next-to-last date.");
    }

    private static void ValidateBusinessDayConvention(BusinessDayConvention convention, string parameterName)
    {
        if (convention is not (BusinessDayConvention.Unadjusted
            or BusinessDayConvention.Following
            or BusinessDayConvention.ModifiedFollowing
            or BusinessDayConvention.Preceding
            or BusinessDayConvention.ModifiedPreceding))
        {
            throw new ArgumentOutOfRangeException(parameterName, convention, "Unsupported business-day convention.");
        }
    }

    private static void ValidateDateGenerationRule(DateGenerationRule rule)
    {
        if (rule is not (DateGenerationRule.Zero or DateGenerationRule.Forward or DateGenerationRule.Backward))
            throw new ArgumentOutOfRangeException(nameof(rule), rule, "Unsupported date generation rule.");
    }
}
