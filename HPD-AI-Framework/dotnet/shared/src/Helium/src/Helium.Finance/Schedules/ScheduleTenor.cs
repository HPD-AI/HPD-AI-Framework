namespace Helium.Finance.Schedules;

public readonly record struct ScheduleTenor
{
    private readonly int _length;
    private readonly TenorUnit _unit;

    public ScheduleTenor(int Length, TenorUnit Unit)
    {
        if (Length < 0)
            throw new ArgumentOutOfRangeException(nameof(Length), "Tenor length must be nonnegative.");

        if (Unit is not (TenorUnit.Days or TenorUnit.Weeks or TenorUnit.Months or TenorUnit.Years))
            throw new ArgumentOutOfRangeException(nameof(Unit), Unit, "Unsupported tenor unit.");

        _length = Length;
        _unit = Unit;
    }

    public static ScheduleTenor Zero { get; } = new(0, TenorUnit.Days);

    public static ScheduleTenor Days(int length) => new(length, TenorUnit.Days);

    public static ScheduleTenor Weeks(int length) => new(length, TenorUnit.Weeks);

    public static ScheduleTenor Months(int length) => new(length, TenorUnit.Months);

    public static ScheduleTenor Years(int length) => new(length, TenorUnit.Years);

    public static ScheduleTenor Monthly { get; } = Months(1);

    public static ScheduleTenor Quarterly { get; } = Months(3);

    public static ScheduleTenor Semiannual { get; } = Months(6);

    public static ScheduleTenor Annual { get; } = Years(1);

    public int Length
    {
        get => _length;
        init
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Tenor length must be nonnegative.");

            _length = value;
        }
    }

    public TenorUnit Unit
    {
        get => _unit;
        init
        {
            if (value is not (TenorUnit.Days or TenorUnit.Weeks or TenorUnit.Months or TenorUnit.Years))
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported tenor unit.");

            _unit = value;
        }
    }

    public bool AllowsEndOfMonth => Length > 0 && (Unit == TenorUnit.Months || Unit == TenorUnit.Years);

    public void Validate()
    {
        if (Length < 0)
            throw new ArgumentOutOfRangeException(nameof(Length), "Tenor length must be nonnegative.");

        if (Unit is not (TenorUnit.Days or TenorUnit.Weeks or TenorUnit.Months or TenorUnit.Years))
            throw new ArgumentOutOfRangeException(nameof(Unit), Unit, "Unsupported tenor unit.");
    }

    public DateOnly AddTo(DateOnly date, int periods, bool endOfMonth)
    {
        Validate();

        try
        {
            checked
            {
                var multiplier = Length * periods;
                return Unit switch
                {
                    TenorUnit.Days => date.AddDays(multiplier),
                    TenorUnit.Weeks => date.AddDays(multiplier * 7),
                    TenorUnit.Months => AddMonths(date, multiplier, endOfMonth),
                    TenorUnit.Years => AddMonths(date, multiplier * 12, endOfMonth),
                    _ => throw new ArgumentOutOfRangeException(nameof(Unit), Unit, "Unsupported tenor unit.")
                };
            }
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(nameof(periods), periods, "Tenor arithmetic overflowed the supported date range.");
        }
        catch (ArgumentOutOfRangeException exception) when (exception.ParamName is null or "value" or "months")
        {
            throw new ArgumentOutOfRangeException(nameof(periods), periods, "Tenor arithmetic overflowed the supported date range.");
        }
    }

    public void Deconstruct(out int Length, out TenorUnit Unit)
    {
        Length = this.Length;
        Unit = this.Unit;
    }

    private static DateOnly AddMonths(DateOnly date, int months, bool endOfMonth)
    {
        var shifted = date.AddMonths(months);
        if (!endOfMonth)
            return shifted;

        return new DateOnly(shifted.Year, shifted.Month, DateTime.DaysInMonth(shifted.Year, shifted.Month));
    }
}
