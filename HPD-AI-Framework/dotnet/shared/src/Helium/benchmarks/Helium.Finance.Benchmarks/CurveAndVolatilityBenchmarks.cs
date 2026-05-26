using BenchmarkDotNet.Attributes;
using Helium.Finance.Calendars;
using Helium.Finance.CashFlows;
using Helium.Finance.Conventions;
using Helium.Finance.Curves;
using Helium.Finance.Schedules;
using Helium.Finance.Volatility;

namespace Helium.Finance.Benchmarks;

[MemoryDiagnoser]
[SimpleJob]
public class CurveAndVolatilityBenchmarks
{
    private DiscountCurve _discountCurve = null!;
    private CurvePoint[] _discountCurvePoints = [];
    private InterestRate _interestRate;
    private ForwardCurve _forwardCurve = null!;
    private BlackVolatilityCurve _volatilityCurve = null!;
    private BlackVarianceCurve _varianceCurve = null!;
    private BlackVolatilitySurface _surface = null!;
    private BlackVarianceSurface _varianceSurface = null!;
    private double[] _surfaceTimes = [];
    private double[] _surfaceStrikes = [];
    private double[,] _surfaceValues = new double[0, 0];
    private ScheduleInput _monthlyScheduleInput;
    private CashFlowLeg _fixedLeg = null!;
    private FixedRateCashFlowLeg _fixedRateLeg = null!;
    private FixedRateCashFlowLeg _compoundedFixedRateLeg = null!;
    private FixedRateBond _fixedRateBond = null!;
    private FixedRateBond _compoundedFixedRateBond = null!;
    private FixedRateBond _amortizingFixedRateBond = null!;
    private double _fixedLegTargetNpv;
    private double _fixedRateBondCleanPriceWithSpread;
    private DateOnly[] _dayCountStarts = [];
    private DateOnly[] _dayCountEnds = [];

    [GlobalSetup]
    public void Setup()
    {
        _discountCurvePoints =
        [
            new CurvePoint(0.0, 1.00),
            new CurvePoint(0.5, 0.985),
            new CurvePoint(1.0, 0.960),
            new CurvePoint(2.0, 0.910),
            new CurvePoint(5.0, 0.780)
        ];

        _discountCurve = new DiscountCurve(
        _discountCurvePoints,
        extrapolationPolicy: ExtrapolationPolicy.Flat);

        _interestRate = new InterestRate(0.0525, CompoundingConvention.Compounded, Frequency: 2);
        _forwardCurve = ForwardCurve.FromSpotAndDiscountCurves(
            spot: 100.0,
            times: [0.0, 0.5, 1.0, 2.0, 5.0],
            fundingCurve: _discountCurve,
            incomeCurve: new DiscountCurve(
            [
                new CurvePoint(0.0, 1.00),
                new CurvePoint(0.5, 0.990),
                new CurvePoint(1.0, 0.980),
                new CurvePoint(2.0, 0.955),
                new CurvePoint(5.0, 0.880)
            ],
            extrapolationPolicy: ExtrapolationPolicy.Flat),
            extrapolationPolicy: ExtrapolationPolicy.Flat);

        _volatilityCurve = new BlackVolatilityCurve(
        [
            new CurvePoint(0.25, 0.18),
            new CurvePoint(0.50, 0.20),
            new CurvePoint(1.00, 0.22),
            new CurvePoint(2.00, 0.26)
        ],
        extrapolationPolicy: ExtrapolationPolicy.Flat);

        _varianceCurve = BlackVarianceCurve.FromVolatilities(
        [
            new CurvePoint(0.25, 0.18),
            new CurvePoint(0.50, 0.20),
            new CurvePoint(1.00, 0.22),
            new CurvePoint(2.00, 0.26)
        ],
        extrapolationPolicy: ExtrapolationPolicy.Flat);

        _surfaceTimes = [0.25, 0.50, 1.00, 2.00];
        _surfaceStrikes = [80.0, 90.0, 100.0, 110.0, 120.0];
        _surfaceValues = new[,]
        {
            { 0.31, 0.27, 0.23, 0.25, 0.29 },
            { 0.29, 0.25, 0.22, 0.24, 0.28 },
            { 0.27, 0.24, 0.21, 0.23, 0.26 },
            { 0.26, 0.23, 0.20, 0.22, 0.25 }
        };

        _surface = new BlackVolatilitySurface(
            times: _surfaceTimes,
            strikes: _surfaceStrikes,
            values: _surfaceValues,
            extrapolationPolicy: ExtrapolationPolicy.Flat);

        _varianceSurface = BlackVarianceSurface.FromVolatilities(
            times: _surfaceTimes,
            strikes: _surfaceStrikes,
            volatilities: _surfaceValues,
            extrapolationPolicy: ExtrapolationPolicy.Flat);

        _monthlyScheduleInput = ScheduleInput.Forward(
            new DateOnly(2026, 1, 31),
            new DateOnly(2036, 1, 31),
            ScheduleTenor.Monthly,
            calendar: HolidayCalendar.WeekendsOnly,
            convention: BusinessDayConvention.ModifiedFollowing,
            endOfMonth: true);

        var fixedSchedule = ScheduleGenerator.Generate(ScheduleInput.Forward(
            new DateOnly(2026, 1, 1),
            new DateOnly(2036, 1, 1),
            ScheduleTenor.Semiannual,
            calendar: HolidayCalendar.WeekendsOnly,
            convention: BusinessDayConvention.ModifiedFollowing));
        var fixedCoupons = FixedRateCashFlow.FromSchedule(
            fixedSchedule,
            nominal: 1_000_000.0,
            rate: 0.045,
            DayCountConvention.Actual365Fixed);
        _fixedRateLeg = new FixedRateCashFlowLeg(fixedCoupons);
        _compoundedFixedRateLeg = new FixedRateCashFlowLeg(FixedRateCashFlow.FromSchedule(
            fixedSchedule,
            nominal: 1_000_000.0,
            rate: 0.045,
            DayCountConvention.Actual365Fixed,
            compounding: CompoundingConvention.Compounded,
            frequency: 2));
        _fixedRateBond = new FixedRateBond(
            faceAmount: 1_000_000.0,
            coupons: _fixedRateLeg,
            maturityDate: fixedSchedule.EndDate,
            issueDate: fixedSchedule.StartDate);
        _compoundedFixedRateBond = new FixedRateBond(
            faceAmount: 1_000_000.0,
            coupons: _compoundedFixedRateLeg,
            maturityDate: fixedSchedule.EndDate,
            issueDate: fixedSchedule.StartDate);
        _amortizingFixedRateBond = FixedRateBond.FromSchedule(
            notionals: AmortizationNotionals.French(
                initialNotional: 1_000_000.0,
                annualCouponRate: 0.045,
                paymentsPerYear: 2,
                periodCount: fixedSchedule.Dates.Count - 1),
            schedule: fixedSchedule,
            couponRate: 0.045,
            dayCountConvention: DayCountConvention.Actual365Fixed,
            issueDate: fixedSchedule.StartDate);
        _fixedLeg = new CashFlowLeg(fixedCoupons.Select(coupon => coupon.ToSimpleCashFlow()));
        _fixedLegTargetNpv = CashFlowValuation.NetPresentValue(
            _fixedLeg,
            new InterestRate(0.045, CompoundingConvention.Continuous),
            referenceDate: new DateOnly(2026, 1, 1),
            DayCountConvention.Actual365Fixed,
            settlementDate: new DateOnly(2026, 1, 1));
        _fixedRateBondCleanPriceWithSpread = BondValuation.CleanPriceWithContinuousSpread(
            _fixedRateBond,
            _discountCurve,
            continuousSpread: 0.01,
            referenceDate: new DateOnly(2026, 1, 1),
            DayCountConvention.Actual365Fixed,
            settlementDate: new DateOnly(2028, 4, 1));

        _dayCountStarts =
        [
            new DateOnly(2024, 2, 29),
            new DateOnly(2023, 8, 31),
            new DateOnly(2024, 2, 28),
            new DateOnly(2026, 1, 15)
        ];
        _dayCountEnds =
        [
            new DateOnly(2024, 3, 31),
            new DateOnly(2024, 2, 29),
            new DateOnly(2024, 3, 31),
            new DateOnly(2026, 1, 31)
        ];
    }

    [Benchmark]
    public double DiscountCurveLookup() => _discountCurve.DiscountFactor(1.4);

    [Benchmark]
    public CurveValidationResult DiscountCurveValidation() =>
        CurveValidation.ValidateDiscountCurvePoints(_discountCurvePoints, requireTimeZero: true);

    [Benchmark]
    public InterestRate EquivalentContinuousRate() =>
        _interestRate.EquivalentRate(CompoundingConvention.Continuous, 2.25);

    [Benchmark]
    public double TransitionCompoundingDiscountFactors()
    {
        var simpleThenCompounded = new InterestRate(0.061, CompoundingConvention.SimpleThenCompounded, Frequency: 4);
        var compoundedThenSimple = new InterestRate(0.061, CompoundingConvention.CompoundedThenSimple, Frequency: 4);
        return simpleThenCompounded.DiscountFactor(0.2)
            + simpleThenCompounded.DiscountFactor(1.75)
            + compoundedThenSimple.DiscountFactor(0.2)
            + compoundedThenSimple.DiscountFactor(1.75);
    }

    [Benchmark]
    public double DayCountConventionSet()
    {
        var sum = 0.0;
        sum += DayCounts.YearFraction(_dayCountStarts[0], _dayCountEnds[0], DayCountConvention.Thirty360Us).Value;
        sum += DayCounts.YearFraction(
            _dayCountStarts[1],
            _dayCountEnds[1],
            DayCountConvention.ThirtyE360Isda,
            new DayCountOptions(TerminationDate: _dayCountEnds[1])).Value;
        sum += DayCounts.YearFraction(_dayCountStarts[2], _dayCountEnds[2], DayCountConvention.Thirty360Italian).Value;
        sum += DayCounts.YearFraction(_dayCountStarts[3], _dayCountEnds[3], DayCountConvention.Thirty360Nasd).Value;
        sum += DayCounts.YearFraction(new DateOnly(2024, 2, 28), new DateOnly(2024, 3, 1), DayCountConvention.ActualActualAfb).Value;
        sum += DayCounts.YearFraction(new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 9), DayCountConvention.Business252).Value;
        return sum;
    }

    [Benchmark]
    public double ForwardCurveLookup() => _forwardCurve.Forward(1.4);

    [Benchmark]
    public double ForwardCurveContinuousReturn() =>
        _forwardCurve.ContinuouslyCompoundedForwardReturn(1.0, 2.0);

    [Benchmark]
    public double VolatilityCurveLookup() => _volatilityCurve.Volatility(0.8);

    [Benchmark]
    public double BlackVarianceCurveLookup() => _varianceCurve.Variance(0.8);

    [Benchmark]
    public double BlackVarianceForwardVolatility() => _varianceCurve.ForwardVolatility(0.5, 2.0);

    [Benchmark]
    public double VolatilitySurfaceLookup() => _surface.Volatility(0.8, 103.0);

    [Benchmark]
    public double BlackVarianceSurfaceLookup() => _varianceSurface.Variance(0.8, 103.0);

    [Benchmark]
    public double BlackVarianceSurfaceForwardVolatility() => _varianceSurface.ForwardVolatility(0.5, 2.0, 103.0);

    [Benchmark]
    public VolatilitySurfaceValidationResult ValidateVolatilitySurface() =>
        VolatilitySurfaceValidation.ValidateBlackSurface(_surfaceTimes, _surfaceStrikes, _surfaceValues);

    [Benchmark]
    public DateSchedule MonthlyScheduleGeneration() => ScheduleGenerator.Generate(_monthlyScheduleInput);

    [Benchmark]
    public double FixedLegNpv() => CashFlowValuation.NetPresentValue(
        _fixedLeg,
        _discountCurve,
        referenceDate: new DateOnly(2026, 1, 1),
        DayCountConvention.Actual365Fixed,
        settlementDate: new DateOnly(2026, 1, 1));

    [Benchmark]
    public double FixedRateLegAccruedAmount() => _fixedRateLeg.AccruedAmount(new DateOnly(2028, 4, 1));

    [Benchmark]
    public double CompoundedFixedRateLegAccruedAmount() => _compoundedFixedRateLeg.AccruedAmount(new DateOnly(2028, 4, 1));

    [Benchmark]
    public double CompoundedFixedRateBondCouponBasisPointValue() => BondValuation.CouponBasisPointValue(
        _compoundedFixedRateBond,
        _discountCurve,
        referenceDate: new DateOnly(2026, 1, 1),
        DayCountConvention.Actual365Fixed,
        settlementDate: new DateOnly(2028, 4, 1));

    [Benchmark]
    public double FixedRateBondCleanPrice() => BondValuation.CleanPrice(
        _fixedRateBond,
        _interestRate,
        referenceDate: new DateOnly(2026, 1, 1),
        DayCountConvention.Actual365Fixed,
        settlementDate: new DateOnly(2028, 4, 1));

    [Benchmark]
    public double AmortizingFixedRateBondDirtyPrice() => BondValuation.DirtyPrice(
        _amortizingFixedRateBond,
        _interestRate,
        referenceDate: new DateOnly(2026, 1, 1),
        DayCountConvention.Actual365Fixed,
        settlementDate: new DateOnly(2028, 4, 1));

    [Benchmark]
    public IReadOnlyList<double> FrenchAmortizationNotionals() => AmortizationNotionals.French(
        initialNotional: 1_000_000.0,
        annualCouponRate: 0.045,
        paymentsPerYear: 2,
        periodCount: 20);

    [Benchmark]
    public BondSpreadResult FixedRateBondContinuousZSpread() => BondValuation.ContinuousZSpread(
        _fixedRateBond,
        _fixedRateBondCleanPriceWithSpread,
        BondPriceType.Clean,
        _discountCurve,
        referenceDate: new DateOnly(2026, 1, 1),
        DayCountConvention.Actual365Fixed,
        settlementDate: new DateOnly(2028, 4, 1),
        lower: -0.05,
        upper: 0.05);

    [Benchmark]
    public BondSpreadResult FixedRateBondCompoundedZSpread() => BondValuation.ZSpread(
        _fixedRateBond,
        BondValuation.CleanPriceWithSpread(
            _fixedRateBond,
            _discountCurve,
            spread: 0.01,
            CompoundingConvention.Compounded,
            frequency: 2,
            referenceDate: new DateOnly(2026, 1, 1),
            DayCountConvention.Actual365Fixed,
            settlementDate: new DateOnly(2028, 4, 1)),
        BondPriceType.Clean,
        _discountCurve,
        CompoundingConvention.Compounded,
        frequency: 2,
        referenceDate: new DateOnly(2026, 1, 1),
        DayCountConvention.Actual365Fixed,
        settlementDate: new DateOnly(2028, 4, 1),
        lower: -0.05,
        upper: 0.05);

    [Benchmark]
    public double FixedRateBondBasisPointValue() => BondValuation.BasisPointValue(
        _fixedRateBond,
        _interestRate,
        referenceDate: new DateOnly(2026, 1, 1),
        DayCountConvention.Actual365Fixed,
        settlementDate: new DateOnly(2028, 4, 1));

    [Benchmark]
    public double TransitionCompoundingFixedLegDuration() => CashFlowValuation.Duration(
        _fixedLeg,
        new InterestRate(0.0525, CompoundingConvention.SimpleThenCompounded, Frequency: 4),
        referenceDate: new DateOnly(2026, 1, 1),
        DayCountConvention.Actual365Fixed,
        DurationType.Modified,
        settlementDate: new DateOnly(2028, 4, 1),
        npvDate: new DateOnly(2028, 4, 1));

    [Benchmark]
    public double FixedRateBondYieldValueBasisPoint() => BondValuation.YieldValueBasisPoint(
        _fixedRateBond,
        _interestRate,
        referenceDate: new DateOnly(2026, 1, 1),
        DayCountConvention.Actual365Fixed,
        settlementDate: new DateOnly(2028, 4, 1));

    [Benchmark]
    public double FixedRateBondCouponBasisPointValue() => BondValuation.CouponBasisPointValue(
        _fixedRateBond,
        _discountCurve,
        referenceDate: new DateOnly(2026, 1, 1),
        DayCountConvention.Actual365Fixed,
        settlementDate: new DateOnly(2028, 4, 1));

    [Benchmark]
    public double FixedRateBondParCouponRate() => BondValuation.ParCouponRate(
        _fixedRateBond,
        price: 100.0,
        BondPriceType.Clean,
        _discountCurve,
        referenceDate: new DateOnly(2026, 1, 1),
        DayCountConvention.Actual365Fixed,
        settlementDate: new DateOnly(2028, 4, 1));

    [Benchmark]
    public CashFlowYieldResult FixedLegYieldSolve() => CashFlowValuation.Yield(
        _fixedLeg,
        _fixedLegTargetNpv,
        referenceDate: new DateOnly(2026, 1, 1),
        DayCountConvention.Actual365Fixed,
        CompoundingConvention.Continuous,
        settlementDate: new DateOnly(2026, 1, 1),
        lower: -0.25,
        upper: 0.50);

    [Benchmark]
    public double FixedLegModifiedDuration() => CashFlowValuation.Duration(
        _fixedLeg,
        new InterestRate(0.045, CompoundingConvention.Continuous),
        referenceDate: new DateOnly(2026, 1, 1),
        DayCountConvention.Actual365Fixed,
        DurationType.Modified,
        settlementDate: new DateOnly(2026, 1, 1));
}
