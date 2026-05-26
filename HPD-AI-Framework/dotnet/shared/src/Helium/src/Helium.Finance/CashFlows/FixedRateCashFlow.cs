using Helium.Finance.Calendars;
using Helium.Finance.Conventions;
using Helium.Finance.Schedules;

namespace Helium.Finance.CashFlows;

public readonly record struct FixedRateCashFlow
{
    private readonly DateOnly _paymentDate;
    private readonly DateOnly _accrualStartDate;
    private readonly DateOnly _accrualEndDate;
    private readonly double _nominal;
    private readonly double _rate;
    private readonly double _accrualYearFraction;
    private readonly DayCountConvention _dayCountConvention;
    private readonly CompoundingConvention _compounding;
    private readonly int _frequency;

    public FixedRateCashFlow(
        DateOnly PaymentDate,
        DateOnly AccrualStartDate,
        DateOnly AccrualEndDate,
        double Nominal,
        double Rate,
        double AccrualYearFraction,
        DayCountConvention DayCountConvention = DayCountConvention.Actual365Fixed,
        CompoundingConvention Compounding = CompoundingConvention.Simple,
        int Frequency = 1,
        DateOnly? ExCouponDate = null)
    {
        _paymentDate = PaymentDate;
        _accrualStartDate = AccrualStartDate;
        _accrualEndDate = AccrualEndDate;
        _nominal = Nominal;
        _rate = Rate;
        _accrualYearFraction = AccrualYearFraction;
        _dayCountConvention = default;
        _compounding = default;
        _frequency = Frequency;
        this.DayCountConvention = DayCountConvention;
        this.Compounding = Compounding;
        this.ExCouponDate = ExCouponDate;

        Validate();
    }

    public DateOnly PaymentDate
    {
        get => _paymentDate;
        init
        {
            if (_accrualEndDate != default && value < _accrualEndDate)
                throw new ArgumentOutOfRangeException(nameof(value), "Payment date must be on or after accrual end date.");

            _paymentDate = value;
        }
    }

    public DateOnly AccrualStartDate
    {
        get => _accrualStartDate;
        init
        {
            if (_accrualEndDate != default && value > _accrualEndDate)
                throw new ArgumentOutOfRangeException(nameof(value), "Accrual start date must be on or before accrual end date.");

            _accrualStartDate = value;
        }
    }

    public DateOnly AccrualEndDate
    {
        get => _accrualEndDate;
        init
        {
            if (value < _accrualStartDate)
                throw new ArgumentOutOfRangeException(nameof(value), "Accrual end date must be on or after accrual start date.");

            if (_paymentDate != default && _paymentDate < value)
                throw new ArgumentOutOfRangeException(nameof(value), "Accrual end date must be on or before payment date.");

            _accrualEndDate = value;
        }
    }

    public double Nominal
    {
        get => _nominal;
        init
        {
            if (!double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Nominal must be finite.");

            _nominal = value;
        }
    }

    public double Rate
    {
        get => _rate;
        init
        {
            if (!double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Rate must be finite.");

            _rate = value;
        }
    }

    public double AccrualYearFraction
    {
        get => _accrualYearFraction;
        init
        {
            if (!double.IsFinite(value) || value < 0.0)
                throw new ArgumentOutOfRangeException(nameof(value), "Accrual year fraction must be finite and nonnegative.");

            _accrualYearFraction = value;
        }
    }

    public DayCountConvention DayCountConvention
    {
        get => _dayCountConvention;
        init
        {
            ValidateDayCountConvention(value, nameof(value));
            _dayCountConvention = value;
        }
    }

    public CompoundingConvention Compounding
    {
        get => _compounding;
        init
        {
            ValidateCompoundingConvention(value, nameof(value));
            _compounding = value;
        }
    }

    public int Frequency
    {
        get => _frequency;
        init
        {
            _ = new InterestRate(Rate, Compounding, value).CompoundFactor(0.0);
            _frequency = value;
        }
    }

    public DateOnly? ExCouponDate { get; init; }

    public double Amount => MultiplyFinite(
        Nominal,
        InterestAmount(AccrualYearFraction),
        nameof(Nominal),
        "Fixed-rate cash-flow amount must be finite.");

    public SimpleCashFlow ToSimpleCashFlow() => new(PaymentDate, Amount);

    public bool TradingExCoupon(DateOnly settlementDate) => ExCouponDate is { } exCouponDate && exCouponDate <= settlementDate;

    public double AccruedYearFraction(DateOnly settlementDate)
    {
        if (settlementDate <= AccrualStartDate || settlementDate > PaymentDate)
            return 0.0;

        if (TradingExCoupon(settlementDate))
        {
            var remainingAccrualEnd = settlementDate > AccrualEndDate ? settlementDate : AccrualEndDate;
            return -DayCounts.YearFraction(settlementDate, remainingAccrualEnd, DayCountConvention).Value;
        }

        var accruedEnd = settlementDate < AccrualEndDate ? settlementDate : AccrualEndDate;
        return DayCounts.YearFraction(AccrualStartDate, accruedEnd, DayCountConvention).Value;
    }

    public int AccruedDays(DateOnly settlementDate)
    {
        if (settlementDate <= AccrualStartDate || settlementDate > PaymentDate)
            return 0;

        var accruedEnd = settlementDate < AccrualEndDate ? settlementDate : AccrualEndDate;
        return accruedEnd.DayNumber - AccrualStartDate.DayNumber;
    }

    public double AccruedAmount(DateOnly settlementDate) => MultiplyFinite(
        Nominal,
        InterestAmount(AccruedYearFraction(settlementDate)),
        nameof(Nominal),
        "Fixed-rate accrued amount must be finite.");

    public void Validate()
    {
        if (AccrualStartDate > AccrualEndDate)
            throw new ArgumentOutOfRangeException(nameof(AccrualEndDate), "Accrual end date must be on or after accrual start date.");

        if (PaymentDate < AccrualEndDate)
            throw new ArgumentOutOfRangeException(nameof(PaymentDate), "Payment date must be on or after accrual end date.");

        if (!double.IsFinite(Nominal))
            throw new ArgumentOutOfRangeException(nameof(Nominal), "Nominal must be finite.");

        if (!double.IsFinite(Rate))
            throw new ArgumentOutOfRangeException(nameof(Rate), "Rate must be finite.");

        if (!double.IsFinite(AccrualYearFraction) || AccrualYearFraction < 0.0)
            throw new ArgumentOutOfRangeException(nameof(AccrualYearFraction), "Accrual year fraction must be finite and nonnegative.");

        _ = DayCounts.YearFraction(AccrualStartDate, AccrualEndDate, DayCountConvention);
        _ = new InterestRate(Rate, Compounding, Frequency).CompoundFactor(AccrualYearFraction);
    }

    public static IReadOnlyList<FixedRateCashFlow> FromSchedule(
        DateSchedule schedule,
        double nominal,
        double rate,
        DayCountConvention dayCountConvention,
        int exCouponBusinessDays = 0,
        HolidayCalendar? exCouponCalendar = null,
        BusinessDayConvention exCouponConvention = BusinessDayConvention.Preceding,
        int paymentLagBusinessDays = 0,
        HolidayCalendar? paymentCalendar = null,
        BusinessDayConvention paymentConvention = BusinessDayConvention.Following,
        CompoundingConvention compounding = CompoundingConvention.Simple,
        int frequency = 1)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        if (!double.IsFinite(nominal))
            throw new ArgumentOutOfRangeException(nameof(nominal), "Nominal must be finite.");

        if (!double.IsFinite(rate))
            throw new ArgumentOutOfRangeException(nameof(rate), "Rate must be finite.");

        _ = new InterestRate(rate, compounding, frequency).CompoundFactor(0.0);

        if (exCouponBusinessDays < 0)
            throw new ArgumentOutOfRangeException(nameof(exCouponBusinessDays), "Ex-coupon business days must be nonnegative.");

        if (paymentLagBusinessDays < 0)
            throw new ArgumentOutOfRangeException(nameof(paymentLagBusinessDays), "Payment lag business days must be nonnegative.");

        var exCalendar = exCouponCalendar ?? schedule.Input.Calendar;
        var payCalendar = paymentCalendar ?? schedule.Input.Calendar;
        var flows = new FixedRateCashFlow[schedule.Dates.Count - 1];
        for (var i = 0; i < flows.Length; i++)
        {
            var accrualStart = schedule.Dates[i];
            var accrualEnd = schedule.Dates[i + 1];
            var accrual = DayCounts.YearFraction(accrualStart, accrualEnd, dayCountConvention).Value;
            var paymentDate = paymentLagBusinessDays == 0
                ? accrualEnd
                : payCalendar.Adjust(payCalendar.AdvanceBusinessDays(accrualEnd, paymentLagBusinessDays), paymentConvention);
            var exCouponDate = exCouponBusinessDays == 0
                ? (DateOnly?)null
                : exCalendar.Adjust(exCalendar.AdvanceBusinessDays(paymentDate, -exCouponBusinessDays), exCouponConvention);

            flows[i] = new FixedRateCashFlow(
                PaymentDate: paymentDate,
                AccrualStartDate: accrualStart,
                AccrualEndDate: accrualEnd,
                Nominal: nominal,
                Rate: rate,
                AccrualYearFraction: accrual,
                DayCountConvention: dayCountConvention,
                Compounding: compounding,
                Frequency: frequency,
                ExCouponDate: exCouponDate);
        }

        return flows;
    }

    public void Deconstruct(
        out DateOnly PaymentDate,
        out DateOnly AccrualStartDate,
        out DateOnly AccrualEndDate,
        out double Nominal,
        out double Rate,
        out double AccrualYearFraction,
        out DayCountConvention DayCountConvention,
        out CompoundingConvention Compounding,
        out int Frequency,
        out DateOnly? ExCouponDate)
    {
        PaymentDate = this.PaymentDate;
        AccrualStartDate = this.AccrualStartDate;
        AccrualEndDate = this.AccrualEndDate;
        Nominal = this.Nominal;
        Rate = this.Rate;
        AccrualYearFraction = this.AccrualYearFraction;
        DayCountConvention = this.DayCountConvention;
        Compounding = this.Compounding;
        Frequency = this.Frequency;
        ExCouponDate = this.ExCouponDate;
    }

    private double InterestAmount(double time)
    {
        var sign = time < 0.0 ? -1.0 : 1.0;
        var magnitude = Math.Abs(time);
        var amount = sign * (new InterestRate(Rate, Compounding, Frequency).CompoundFactor(magnitude) - 1.0);
        if (!double.IsFinite(amount))
            throw new ArgumentOutOfRangeException(nameof(time), "Fixed-rate interest amount must be finite.");

        return amount;
    }

    private static double MultiplyFinite(double left, double right, string parameterName, string message)
    {
        var result = left * right;
        if (!double.IsFinite(result))
            throw new ArgumentOutOfRangeException(parameterName, message);

        return result;
    }

    private static void ValidateDayCountConvention(DayCountConvention convention, string parameterName)
    {
        if (convention is not (DayCountConvention.Actual360
            or DayCountConvention.Actual365Fixed
            or DayCountConvention.Business252
            or DayCountConvention.ActualActualIsda
            or DayCountConvention.ActualActualAfb
            or DayCountConvention.Thirty360Us
            or DayCountConvention.Thirty360BondBasis
            or DayCountConvention.ThirtyE360
            or DayCountConvention.ThirtyE360Isda
            or DayCountConvention.Thirty360Italian
            or DayCountConvention.Thirty360Nasd))
        {
            throw new ArgumentOutOfRangeException(parameterName, convention, "Unsupported day-count convention.");
        }
    }

    private static void ValidateCompoundingConvention(CompoundingConvention convention, string parameterName)
    {
        if (convention is not (CompoundingConvention.Simple
            or CompoundingConvention.Continuous
            or CompoundingConvention.Compounded
            or CompoundingConvention.SimpleThenCompounded
            or CompoundingConvention.CompoundedThenSimple))
        {
            throw new ArgumentOutOfRangeException(parameterName, convention, "Unsupported compounding convention.");
        }
    }
}
