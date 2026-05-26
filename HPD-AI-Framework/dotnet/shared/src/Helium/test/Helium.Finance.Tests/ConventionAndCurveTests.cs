using Helium.Finance.Conventions;
using Helium.Finance.Curves;

namespace Helium.Finance.Tests;

public class ConventionAndCurveTests
{
    [Fact]
    public void Actual360ComputesSimpleYearFraction()
    {
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 7, 1);

        var yearFraction = DayCounts.YearFraction(start, end, DayCountConvention.Actual360);

        AssertClose(181.0 / 360.0, yearFraction.Value, 1e-15);
    }

    [Fact]
    public void Actual365FixedHandlesLeapYearByActualDays()
    {
        var start = new DateOnly(2024, 2, 28);
        var end = new DateOnly(2024, 3, 1);

        var yearFraction = DayCounts.YearFraction(start, end, DayCountConvention.Actual365Fixed);

        AssertClose(2.0 / 365.0, yearFraction.Value, 1e-15);
    }

    [Fact]
    public void Thirty360UsHandlesMonthEnd()
    {
        var start = new DateOnly(2026, 1, 31);
        var end = new DateOnly(2026, 2, 28);

        var yearFraction = DayCounts.YearFraction(start, end, DayCountConvention.Thirty360Us);

        AssertClose(28.0 / 360.0, yearFraction.Value, 1e-15);
    }

    [Fact]
    public void ThirtyE360CapsBothMonthEndDaysAtThirty()
    {
        var start = new DateOnly(2026, 1, 31);
        var end = new DateOnly(2026, 2, 28);

        var yearFraction = DayCounts.YearFraction(start, end, DayCountConvention.ThirtyE360);

        AssertClose(28.0 / 360.0, yearFraction.Value, 1e-15);
    }

    [Fact]
    public void ActualActualIsdaSplitsAcrossLeapYearBoundary()
    {
        var start = new DateOnly(2023, 12, 31);
        var end = new DateOnly(2024, 1, 2);

        var yearFraction = DayCounts.YearFraction(start, end, DayCountConvention.ActualActualIsda);

        AssertClose(1.0 / 365.0 + 1.0 / 366.0, yearFraction.Value, 1e-15);
    }

    [Fact]
    public void ReversedDatesReturnSignedYearFraction()
    {
        var forward = DayCounts.YearFraction(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), DayCountConvention.Actual360);
        var reversed = DayCounts.YearFraction(new DateOnly(2026, 1, 31), new DateOnly(2026, 1, 1), DayCountConvention.Actual360);

        AssertClose(-forward.Value, reversed.Value, 1e-15);
    }

    [Fact]
    public void ContinuousRateDiscountFactorRoundTripsThroughFlatCurve()
    {
        var curve = new FlatDiscountCurve(new InterestRate(0.05, CompoundingConvention.Continuous));

        AssertClose(Math.Exp(-0.05 * 2.0), curve.DiscountFactor(2.0), 1e-15);
        AssertClose(Math.Exp(-0.05), curve.ForwardDiscountFactor(1.0, 2.0), 1e-15);
    }

    [Fact]
    public void FlatDiscountCurveRejectsInvalidForwardIntervals()
    {
        var curve = new FlatDiscountCurve(new InterestRate(0.05, CompoundingConvention.Continuous));

        Assert.Throws<ArgumentOutOfRangeException>(() => curve.ForwardDiscountFactor(-0.1, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.ForwardDiscountFactor(1.0, 0.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.ForwardDiscountFactor(double.NaN, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.ForwardDiscountFactor(0.0, double.PositiveInfinity));
    }

    [Fact]
    public void FlatDiscountCurveRejectsNonfiniteForwardProjection()
    {
        var curve = new FlatDiscountCurve(new InterestRate(1_000.0, CompoundingConvention.Continuous));

        Assert.Throws<ArgumentOutOfRangeException>(() => curve.ForwardDiscountFactor(1.0, 2.0));
    }

    [Fact]
    public void InterestRateRoundTripsThroughDiscountFactor()
    {
        var simple = new InterestRate(0.05, CompoundingConvention.Simple);
        var compounded = new InterestRate(0.05, CompoundingConvention.Compounded, Frequency: 4);
        var continuous = new InterestRate(0.05, CompoundingConvention.Continuous);

        AssertRateRoundTrip(simple, 1.5);
        AssertRateRoundTrip(compounded, 1.5);
        AssertRateRoundTrip(continuous, 1.5);
    }

    [Fact]
    public void InterestRateCompoundFactorIsInverseOfDiscountFactor()
    {
        var rate = new InterestRate(0.0525, CompoundingConvention.Compounded, Frequency: 2);
        var compoundFactor = Math.Pow(1.0 + 0.0525 / 2.0, 2.0 * 2.25);

        AssertClose(compoundFactor, rate.CompoundFactor(2.25), 1e-15);
        AssertClose(1.0, rate.CompoundFactor(2.25) * rate.DiscountFactor(2.25), 1e-15);
    }

    [Fact]
    public void EquivalentRatePreservesDiscountFactor()
    {
        var source = new InterestRate(0.0525, CompoundingConvention.Compounded, Frequency: 2);
        var equivalent = source.EquivalentRate(CompoundingConvention.Continuous, time: 2.25);

        AssertClose(source.DiscountFactor(2.25), equivalent.DiscountFactor(2.25), 1e-15);
    }

    [Fact]
    public void SimpleThenCompoundedUsesSimpleUpToFirstPeriodThenCompounded()
    {
        var rate = new InterestRate(0.06, CompoundingConvention.SimpleThenCompounded, Frequency: 2);

        AssertClose(1.0 / (1.0 + 0.06 * 0.5), rate.DiscountFactor(0.5), 1e-15);
        AssertClose(Math.Pow(1.0 + 0.06 / 2.0, -2.0 * 1.5), rate.DiscountFactor(1.5), 1e-15);
    }

    [Fact]
    public void CompoundedThenSimpleUsesCompoundedUpToFirstPeriodThenSimple()
    {
        var rate = new InterestRate(0.06, CompoundingConvention.CompoundedThenSimple, Frequency: 2);

        AssertClose(Math.Pow(1.0 + 0.06 / 2.0, -2.0 * 0.5), rate.DiscountFactor(0.5), 1e-15);
        AssertClose(1.0 / (1.0 + 0.06 * 1.5), rate.DiscountFactor(1.5), 1e-15);
    }

    [Fact]
    public void TransitionCompoundingRoundTripsThroughDiscountFactor()
    {
        var simpleThenCompounded = new InterestRate(0.061, CompoundingConvention.SimpleThenCompounded, Frequency: 4);
        var compoundedThenSimple = new InterestRate(0.061, CompoundingConvention.CompoundedThenSimple, Frequency: 4);

        AssertRateRoundTrip(simpleThenCompounded, 0.20);
        AssertRateRoundTrip(simpleThenCompounded, 1.75);
        AssertRateRoundTrip(compoundedThenSimple, 0.20);
        AssertRateRoundTrip(compoundedThenSimple, 1.75);
    }

    [Fact]
    public void ZeroRateExtractsContinuousEquivalentRate()
    {
        var rate = new InterestRate(0.06, CompoundingConvention.Simple);
        var zeroRate = rate.ZeroRate(2.0);

        AssertClose(rate.DiscountFactor(2.0), Math.Exp(-zeroRate * 2.0), 1e-15);
    }

    [Fact]
    public void RateExtractionRejectsInvalidDiscountFactor()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InterestRate.FromDiscountFactor(0.0, 1.0, CompoundingConvention.Continuous));
    }

    [Fact]
    public void InterestRateRejectsInvalidConstructionAndMutation()
    {
        var rate = new InterestRate(0.05, CompoundingConvention.Compounded, Frequency: 2);

        Assert.Throws<ArgumentOutOfRangeException>(() => new InterestRate(double.NaN, CompoundingConvention.Continuous));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InterestRate(0.05, (CompoundingConvention)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => new InterestRate(0.05, CompoundingConvention.Compounded, Frequency: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => rate with { Rate = double.PositiveInfinity });
        Assert.Throws<ArgumentOutOfRangeException>(() => rate with { Compounding = (CompoundingConvention)999 });
        Assert.Throws<ArgumentOutOfRangeException>(() => rate with { Frequency = -1 });
    }

    [Fact]
    public void InterestRateRejectsNonpositiveSimpleCompoundFactor()
    {
        var rate = new InterestRate(-0.75, CompoundingConvention.Simple);

        Assert.Throws<ArgumentOutOfRangeException>(() => rate.CompoundFactor(2.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => rate.DiscountFactor(2.0));
    }

    [Fact]
    public void InterestRateRejectsInvalidCompoundedBase()
    {
        var rate = new InterestRate(-5.0, CompoundingConvention.Compounded, Frequency: 4);

        Assert.Throws<ArgumentOutOfRangeException>(() => rate.CompoundFactor(1.3));
        Assert.Throws<ArgumentOutOfRangeException>(() => rate.DiscountFactor(1.3));
    }

    [Fact]
    public void InterestRateRejectsNonfiniteContinuousCompoundFactor()
    {
        var rate = new InterestRate(1_000.0, CompoundingConvention.Continuous);

        Assert.Throws<ArgumentOutOfRangeException>(() => rate.CompoundFactor(1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => rate.DiscountFactor(1.0));
    }

    [Fact]
    public void DiscountCurveHitsPillarsAndInterpolatesLinearly()
    {
        var curve = new DiscountCurve(
        [
            new CurvePoint(0.0, 1.00),
            new CurvePoint(1.0, 0.95),
            new CurvePoint(2.0, 0.90)
        ]);

        AssertClose(0.95, curve.DiscountFactor(1.0), 1e-15);
        AssertClose(0.925, curve.DiscountFactor(1.5), 1e-15);
    }

    [Fact]
    public void CurveConstructorsRejectNullPoints()
    {
        Assert.Throws<ArgumentNullException>(() => new DiscountCurve(null!));
        Assert.Throws<ArgumentNullException>(() => new ZeroCurve(null!));
        Assert.Throws<ArgumentNullException>(() => new ForwardCurve(null!));
    }

    [Fact]
    public void CurveConstructorsRejectUnorderedAndNegativeTimes()
    {
        Assert.Throws<ArgumentException>(() => new DiscountCurve(
        [
            new CurvePoint(1.0, 0.95),
            new CurvePoint(0.0, 1.00)
        ]));
        Assert.Throws<ArgumentException>(() => new ZeroCurve(
        [
            new CurvePoint(2.0, 0.04),
            new CurvePoint(1.0, 0.03)
        ]));
        Assert.Throws<ArgumentException>(() => new ForwardCurve(
        [
            new CurvePoint(1.0, 105.0),
            new CurvePoint(1.0, 106.0)
        ]));
    }

    [Fact]
    public void CurvePointRejectsInvalidCoordinates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CurvePoint(-1.0, 1.05));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CurvePoint(double.NaN, 1.05));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CurvePoint(1.0, double.PositiveInfinity));
    }

    [Fact]
    public void CurveQueriesRejectNegativeTimes()
    {
        var discount = new DiscountCurve(
        [
            new CurvePoint(0.0, 1.00),
            new CurvePoint(1.0, 0.95)
        ]);
        var zero = new ZeroCurve(
        [
            new CurvePoint(0.0, 0.03),
            new CurvePoint(1.0, 0.04)
        ]);
        var forward = new ForwardCurve(
        [
            new CurvePoint(0.0, 100.0),
            new CurvePoint(1.0, 105.0)
        ]);

        Assert.Throws<ArgumentOutOfRangeException>(() => discount.DiscountFactor(-0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => zero.ZeroRate(-0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => forward.Forward(-0.1));
    }

    [Fact]
    public void CurveConstructorsRejectUnsupportedPolicies()
    {
        var points = new[]
        {
            new CurvePoint(0.0, 1.0),
            new CurvePoint(1.0, 0.95)
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => new DiscountCurve(
            points,
            interpolationPolicy: (InterpolationPolicy)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiscountCurve(
            points,
            extrapolationPolicy: (ExtrapolationPolicy)999));

        Assert.Throws<ArgumentOutOfRangeException>(() => new ZeroCurve(
            points,
            interpolationPolicy: (InterpolationPolicy)999));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ForwardCurve(
            points,
            extrapolationPolicy: (ExtrapolationPolicy)999));
    }

    [Fact]
    public void DiscountCurveFailsOutsideRangeWhenExtrapolationDisabled()
    {
        var curve = new DiscountCurve(
        [
            new CurvePoint(1.0, 0.95),
            new CurvePoint(2.0, 0.90)
        ]);

        Assert.Throws<ArgumentOutOfRangeException>(() => curve.DiscountFactor(3.0));
    }

    [Fact]
    public void DiscountCurveFlatExtrapolationUsesBoundaryPillar()
    {
        var curve = new DiscountCurve(
        [
            new CurvePoint(1.0, 0.95),
            new CurvePoint(2.0, 0.90)
        ],
        extrapolationPolicy: ExtrapolationPolicy.Flat);

        AssertClose(0.95, curve.DiscountFactor(0.5), 1e-15);
        AssertClose(0.90, curve.DiscountFactor(3.0), 1e-15);
    }

    [Fact]
    public void DiscountCurveRejectsNonpositiveLinearExtrapolatedDiscountFactor()
    {
        var curve = new DiscountCurve(
        [
            new CurvePoint(1.0, 0.50),
            new CurvePoint(2.0, 0.25)
        ],
        extrapolationPolicy: ExtrapolationPolicy.Linear);

        Assert.Throws<ArgumentOutOfRangeException>(() => curve.DiscountFactor(3.0));
    }

    [Fact]
    public void DiscountCurveExtractsForwardRateFromDiscountRatio()
    {
        var curve = new DiscountCurve(
        [
            new CurvePoint(0.0, 1.0),
            new CurvePoint(1.0, Math.Exp(-0.03)),
            new CurvePoint(2.0, Math.Exp(-0.07))
        ]);

        AssertClose(0.04, curve.ForwardRate(1.0, 2.0), 1e-15);
    }

    [Fact]
    public void DiscountCurveRejectsInvalidZeroRateTimesBeforeExtrapolation()
    {
        var curve = new DiscountCurve(
        [
            new CurvePoint(1.0, 0.95),
            new CurvePoint(2.0, 0.90)
        ],
        extrapolationPolicy: ExtrapolationPolicy.Flat);

        Assert.Throws<ArgumentOutOfRangeException>(() => curve.ZeroRate(0.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.ZeroRate(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.ZeroRate(double.PositiveInfinity));
    }

    [Fact]
    public void DiscountCurveExtractsForwardDiscountFactor()
    {
        var curve = new DiscountCurve(
        [
            new CurvePoint(0.0, 1.0),
            new CurvePoint(1.0, 0.95),
            new CurvePoint(2.0, 0.90)
        ]);

        AssertClose(0.90 / 0.95, curve.ForwardDiscountFactor(1.0, 2.0), 1e-15);
    }

    [Fact]
    public void DiscountCurveRejectsNonFiniteForwardIntervals()
    {
        var curve = new DiscountCurve(
        [
            new CurvePoint(0.0, 1.0),
            new CurvePoint(1.0, 0.95)
        ],
        extrapolationPolicy: ExtrapolationPolicy.Flat);

        Assert.Throws<ArgumentOutOfRangeException>(() => curve.ForwardRate(double.NaN, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.ForwardRate(0.0, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.ForwardDiscountFactor(double.NegativeInfinity, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.ForwardDiscountFactor(0.0, double.NaN));
    }

    [Fact]
    public void DiscountCurveValidationReportsMultipleDiagnostics()
    {
        var result = CurveValidation.ValidateDiscountCurvePoints(
        [
            new CurvePoint(0.0, 1.0),
            new CurvePoint(0.5, 1.01),
            new CurvePoint(0.5, -0.25)
        ]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == CurveDiagnosticCode.IncreasingDiscountFactor);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == CurveDiagnosticCode.DuplicateOrUnorderedTime);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == CurveDiagnosticCode.NonPositiveDiscountFactor);
    }

    [Fact]
    public void DiscountCurveValidationCanAllowIncreasingDiscountFactors()
    {
        var result = CurveValidation.ValidateDiscountCurvePoints(
        [
            new CurvePoint(0.0, 1.0),
            new CurvePoint(1.0, 1.01)
        ],
        requireNonIncreasingDiscountFactors: false);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void DiscountCurveValidationReportsInvalidTimeZeroDiscountFactor()
    {
        var result = CurveValidation.ValidateDiscountCurvePoints(
        [
            new CurvePoint(0.0, 0.99),
            new CurvePoint(1.0, 0.95)
        ]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == CurveDiagnosticCode.InvalidTimeZeroDiscountFactor);
    }

    [Fact]
    public void DiscountCurveValidationCanAllowUnnormalizedTimeZeroDiscountFactor()
    {
        var result = CurveValidation.ValidateDiscountCurvePoints(
        [
            new CurvePoint(0.0, 0.99),
            new CurvePoint(1.0, 0.95)
        ],
        requireTimeZeroDiscountFactorOne: false);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void CurveValidationCanRequireTimeZero()
    {
        var result = CurveValidation.ValidateZeroCurvePoints(
        [
            new CurvePoint(1.0, 0.03),
            new CurvePoint(2.0, 0.04)
        ],
        requireTimeZero: true);

        Assert.False(result.IsValid);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == CurveDiagnosticCode.MissingTimeZero);
    }

    [Fact]
    public void CurveValidationResultSnapshotsDiagnostics()
    {
        var diagnostics = new List<CurveDiagnostic>
        {
            new(CurveDiagnosticCode.NonFiniteValue, 0, "bad value")
        };

        var result = new CurveValidationResult(diagnostics);
        diagnostics.Clear();

        Assert.False(result.IsValid);
        Assert.Single(result.Diagnostics);
        Assert.Equal(CurveDiagnosticCode.NonFiniteValue, result.Diagnostics[0].Code);
    }

    [Fact]
    public void CurveValidationResultRejectsMalformedDiagnostics()
    {
        Assert.Throws<ArgumentException>(() => new CurveValidationResult([default]));
    }

    [Fact]
    public void CurveDiagnosticRejectsEmptyMessage()
    {
        Assert.Throws<ArgumentException>(() => new CurveDiagnostic(
            CurveDiagnosticCode.NonFiniteValue,
            0,
            ""));
    }

    [Fact]
    public void CurveDiagnosticRejectsInvalidCode()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CurveDiagnostic(
            (CurveDiagnosticCode)999,
            0,
            "bad code"));
    }

    [Fact]
    public void CurveDiagnosticRejectsImpossibleIndex()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CurveDiagnostic(
            CurveDiagnosticCode.NonFiniteValue,
            -2,
            "bad index"));
    }

    [Fact]
    public void ZeroCurveConvertsRateToDiscountFactor()
    {
        var curve = new ZeroCurve(
        [
            new CurvePoint(1.0, 0.03),
            new CurvePoint(2.0, 0.05)
        ]);

        AssertClose(0.04, curve.ZeroRate(1.5), 1e-15);
        AssertClose(Math.Exp(-0.04 * 1.5), curve.DiscountFactor(1.5), 1e-15);
    }

    [Fact]
    public void ZeroCurveRejectsNonfiniteDiscountProjection()
    {
        var curve = new ZeroCurve(
        [
            new CurvePoint(1.0, -1_000.0)
        ],
        extrapolationPolicy: ExtrapolationPolicy.Flat);

        Assert.Throws<ArgumentOutOfRangeException>(() => curve.DiscountFactor(1.0));
    }

    [Fact]
    public void ForwardCurveInterpolatesForwardValues()
    {
        var curve = new ForwardCurve(
        [
            new CurvePoint(0.0, 100.0),
            new CurvePoint(1.0, 105.0),
            new CurvePoint(2.0, 111.0)
        ]);

        AssertClose(108.0, curve.Forward(1.5), 1e-15);
    }

    [Fact]
    public void ForwardCurveExtractsSimpleAndContinuousReturns()
    {
        var curve = new ForwardCurve(
        [
            new CurvePoint(0.0, 100.0),
            new CurvePoint(1.0, 105.0),
            new CurvePoint(2.0, 110.25)
        ]);

        AssertClose(0.05, curve.SimpleForwardReturn(0.0, 1.0), 1e-15);
        AssertClose(Math.Log(1.05), curve.ContinuouslyCompoundedForwardReturn(0.0, 1.0), 1e-15);
    }

    [Fact]
    public void ForwardCurveRejectsNonfiniteReturnProjection()
    {
        var curve = new ForwardCurve(
        [
            new CurvePoint(0.0, double.Epsilon),
            new CurvePoint(1.0, double.MaxValue)
        ]);

        Assert.Throws<ArgumentOutOfRangeException>(() => curve.SimpleForwardReturn(0.0, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.ContinuouslyCompoundedForwardReturn(0.0, 1.0));
    }

    [Fact]
    public void ForwardCurveRejectsNonFiniteReturnIntervals()
    {
        var curve = new ForwardCurve(
        [
            new CurvePoint(0.0, 100.0),
            new CurvePoint(1.0, 105.0)
        ],
        extrapolationPolicy: ExtrapolationPolicy.Flat);

        Assert.Throws<ArgumentOutOfRangeException>(() => curve.SimpleForwardReturn(double.NaN, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.SimpleForwardReturn(0.0, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.ContinuouslyCompoundedForwardReturn(double.NegativeInfinity, 1.0));
        Assert.Throws<ArgumentOutOfRangeException>(() => curve.ContinuouslyCompoundedForwardReturn(0.0, double.NaN));
    }

    [Fact]
    public void CostOfCarryForwardUsesFundingAndIncomeDiscountFactors()
    {
        var spot = 100.0;
        var fundingDiscount = Math.Exp(-0.05);
        var incomeDiscount = Math.Exp(-0.02);

        var forward = ForwardCurve.CostOfCarryForward(spot, fundingDiscount, incomeDiscount);

        AssertClose(spot * Math.Exp(0.03), forward, 1e-12);
    }

    [Fact]
    public void CostOfCarryForwardRejectsNonfiniteProjection()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ForwardCurve.CostOfCarryForward(double.MaxValue, double.Epsilon));
    }

    [Fact]
    public void BuildsForwardCurveFromSpotAndDiscountCurves()
    {
        var funding = new DiscountCurve(
        [
            new CurvePoint(0.0, 1.0),
            new CurvePoint(1.0, Math.Exp(-0.05)),
            new CurvePoint(2.0, Math.Exp(-0.10))
        ]);
        var income = new DiscountCurve(
        [
            new CurvePoint(0.0, 1.0),
            new CurvePoint(1.0, Math.Exp(-0.02)),
            new CurvePoint(2.0, Math.Exp(-0.04))
        ]);

        var curve = ForwardCurve.FromSpotAndDiscountCurves(
            100.0,
            [0.0, 1.0, 2.0],
            funding,
            income);

        AssertClose(100.0, curve.Forward(0.0), 1e-15);
        AssertClose(100.0 * Math.Exp(0.03), curve.Forward(1.0), 1e-12);
        AssertClose(100.0 * Math.Exp(0.06), curve.Forward(2.0), 1e-12);
    }

    private static void AssertClose(double expected, double actual, double tolerance) =>
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"Expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}.");

    private static void AssertRateRoundTrip(InterestRate rate, double time)
    {
        var discountFactor = rate.DiscountFactor(time);
        var roundTrip = InterestRate.FromDiscountFactor(discountFactor, time, rate.Compounding, rate.Frequency);

        AssertClose(rate.Rate, roundTrip.Rate, 1e-14);
        AssertClose(discountFactor, roundTrip.DiscountFactor(time), 1e-15);
    }
}
