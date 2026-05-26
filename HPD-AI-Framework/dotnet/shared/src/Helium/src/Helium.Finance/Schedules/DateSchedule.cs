namespace Helium.Finance.Schedules;

public sealed class DateSchedule
{
    public DateSchedule(
        IReadOnlyList<DateOnly> dates,
        IReadOnlyList<bool> isRegular,
        ScheduleInput input)
    {
        ArgumentNullException.ThrowIfNull(dates);
        ArgumentNullException.ThrowIfNull(isRegular);

        if (dates.Count < 2)
            throw new ArgumentException("A schedule must contain at least two dates.", nameof(dates));

        if (isRegular.Count != dates.Count - 1)
            throw new ArgumentException("Regularity flags must contain one value per schedule period.", nameof(isRegular));

        for (var i = 1; i < dates.Count; i++)
        {
            if (dates[i] <= dates[i - 1])
                throw new ArgumentException("Schedule dates must be strictly increasing.", nameof(dates));
        }

        Dates = dates.ToArray();
        IsRegular = isRegular.ToArray();
        Input = input;
    }

    public IReadOnlyList<DateOnly> Dates { get; }

    public IReadOnlyList<bool> IsRegular { get; }

    public ScheduleInput Input { get; }

    public int Count => Dates.Count;

    public DateOnly StartDate => Dates[0];

    public DateOnly EndDate => Dates[^1];

    public DateOnly? PreviousDate(DateOnly referenceDate)
    {
        for (var i = Dates.Count - 1; i >= 0; i--)
        {
            if (Dates[i] < referenceDate)
                return Dates[i];
        }

        return null;
    }

    public DateOnly? NextDate(DateOnly referenceDate)
    {
        for (var i = 0; i < Dates.Count; i++)
        {
            if (Dates[i] > referenceDate)
                return Dates[i];
        }

        return null;
    }
}
