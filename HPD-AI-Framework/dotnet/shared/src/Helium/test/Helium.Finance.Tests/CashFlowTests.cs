using Helium.Finance.Calendars;
using Helium.Finance.CashFlows;
using Helium.Finance.Conventions;
using Helium.Finance.Curves;
using Helium.Finance.Schedules;
using Helium.Finance.Solvers;

namespace Helium.Finance.Tests;

public class CashFlowTests
{
    [Fact]
    public void SimpleCashFlowLegSortsAndExposesDates()
    {
        var leg = new CashFlowLeg(
        [
            new SimpleCashFlow(new DateOnly(2027, 1, 1), 3.0),
            new SimpleCashFlow(new DateOnly(2026, 1, 1), 1.0),
            new SimpleCashFlow(new DateOnly(2026, 7, 1), 2.0)
        ]);

        Assert.Equal(new DateOnly(2026, 1, 1), leg.StartDate);
        Assert.Equal(new DateOnly(2027, 1, 1), leg.MaturityDate);
        Assert.Equal(3, leg.Count);
    }

    [Fact]
    public void CashFlowLegsRejectNullInputs()
    {
        Assert.Throws<ArgumentNullException>(() => new CashFlowLeg(null!));
        Assert.Throws<ArgumentNullException>(() => new FixedRateCashFlowLeg(null!));
    }

    [Fact]
    public void CashFlowPrimitivesRejectInvalidState()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SimpleCashFlow(
            new DateOnly(2026, 1, 1),
            double.NaN));

        var coupon = new FixedRateCashFlow(
            PaymentDate: new DateOnly(2026, 7, 1),
            AccrualStartDate: new DateOnly(2026, 1, 1),
            AccrualEndDate: new DateOnly(2026, 7, 1),
            Nominal: 1_000_000.0,
            Rate: 0.05,
            AccrualYearFraction: 0.5);

        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedRateCashFlow(
            PaymentDate: new DateOnly(2026, 6, 30),
            AccrualStartDate: new DateOnly(2026, 1, 1),
            AccrualEndDate: new DateOnly(2026, 7, 1),
            Nominal: 1_000_000.0,
            Rate: 0.05,
            AccrualYearFraction: 0.5));

        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedRateCashFlow(
            PaymentDate: new DateOnly(2026, 7, 1),
            AccrualStartDate: new DateOnly(2026, 7, 1),
            AccrualEndDate: new DateOnly(2026, 1, 1),
            Nominal: 1_000_000.0,
            Rate: 0.05,
            AccrualYearFraction: 0.5));

        Assert.Throws<ArgumentOutOfRangeException>(() => coupon with { Rate = double.NaN });
        Assert.Throws<ArgumentOutOfRangeException>(() => coupon with { AccrualYearFraction = -0.1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => coupon with { PaymentDate = new DateOnly(2026, 6, 30) });
        Assert.Throws<ArgumentOutOfRangeException>(() => coupon with { DayCountConvention = (DayCountConvention)999 });
        Assert.Throws<ArgumentOutOfRangeException>(() => coupon with { Compounding = (CompoundingConvention)999 });
    }

    [Fact]
    public void CashFlowAggregatesRejectNonfiniteAmounts()
    {
        var paymentDate = new DateOnly(2026, 1, 1);
        var leg = new CashFlowLeg(
        [
            new SimpleCashFlow(paymentDate, double.MaxValue),
            new SimpleCashFlow(paymentDate, double.MaxValue)
        ]);
        var coupon = new FixedRateCashFlow(
            PaymentDate: new DateOnly(2026, 7, 1),
            AccrualStartDate: new DateOnly(2026, 1, 1),
            AccrualEndDate: new DateOnly(2026, 7, 1),
            Nominal: double.MaxValue,
            Rate: 3.0,
            AccrualYearFraction: 0.75);

        Assert.Throws<ArgumentOutOfRangeException>(() => leg.AmountOn(paymentDate));
        Assert.Throws<ArgumentOutOfRangeException>(() => coupon.Amount);
        Assert.Throws<ArgumentOutOfRangeException>(() => coupon.AccruedAmount(new DateOnly(2026, 6, 30)));
    }

    [Fact]
    public void FixedRateBondAndScheduleFactoriesRejectNullInputs()
    {
        var schedule = ScheduleGenerator.Generate(ScheduleInput.Forward(
            new DateOnly(2026, 1, 1),
            new DateOnly(2027, 1, 1),
            ScheduleTenor.Semiannual,
            calendar: HolidayCalendar.WeekendsOnly,
            convention: BusinessDayConvention.Unadjusted));

        Assert.Throws<ArgumentNullException>(() => new FixedRateBond(100.0, null!, new DateOnly(2027, 1, 1)));
        Assert.Throws<ArgumentNullException>(() => FixedRateCashFlow.FromSchedule(
            null!,
            nominal: 100.0,
            rate: 0.05,
            DayCountConvention.Actual365Fixed));
        Assert.Throws<ArgumentNullException>(() => FixedRateBond.FromSchedule(
            100.0,
            null!,
            couponRate: 0.05,
            DayCountConvention.Actual365Fixed));
        Assert.Throws<ArgumentNullException>(() => FixedRateBond.FromSchedule(
            null!,
            schedule,
            couponRate: 0.05,
            DayCountConvention.Actual365Fixed));
        Assert.Throws<ArgumentNullException>(() => FixedRateBond.FromSchedule(
            [100.0, 100.0],
            null!,
            couponRate: 0.05,
            DayCountConvention.Actual365Fixed));
    }

    [Fact]
    public void FixedRateBondRejectsIssueDateOnOrAfterFirstCouponPayment()
    {
        var coupons = new FixedRateCashFlowLeg(
        [
            new FixedRateCashFlow(
                PaymentDate: new DateOnly(2026, 7, 1),
                AccrualStartDate: new DateOnly(2026, 1, 1),
                AccrualEndDate: new DateOnly(2026, 7, 1),
                Nominal: 1_000_000.0,
                Rate: 0.05,
                AccrualYearFraction: 181.0 / 365.0),
            new FixedRateCashFlow(
                PaymentDate: new DateOnly(2027, 1, 1),
                AccrualStartDate: new DateOnly(2026, 7, 1),
                AccrualEndDate: new DateOnly(2027, 1, 1),
                Nominal: 1_000_000.0,
                Rate: 0.05,
                AccrualYearFraction: 184.0 / 365.0)
        ]);

        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedRateBond(
            faceAmount: 1_000_000.0,
            coupons,
            maturityDate: new DateOnly(2027, 1, 1),
            issueDate: new DateOnly(2026, 7, 1)));
    }

    [Fact]
    public void CashFlowLegsSnapshotInputCollections()
    {
        var flows = new List<SimpleCashFlow>
        {
            new(new DateOnly(2026, 1, 1), 1.0)
        };
        var leg = new CashFlowLeg(flows);
        flows[0] = new SimpleCashFlow(new DateOnly(2030, 1, 1), 2.0);

        Assert.Equal(new DateOnly(2026, 1, 1), leg.StartDate);
        Assert.Equal(1.0, leg.Flows[0].Amount);

        var coupons = new List<FixedRateCashFlow>
        {
            new(
                PaymentDate: new DateOnly(2026, 7, 1),
                AccrualStartDate: new DateOnly(2026, 1, 1),
                AccrualEndDate: new DateOnly(2026, 7, 1),
                Nominal: 1_000_000.0,
                Rate: 0.05,
                AccrualYearFraction: 0.5)
        };
        var couponLeg = new FixedRateCashFlowLeg(coupons);
        coupons[0] = coupons[0] with { PaymentDate = new DateOnly(2030, 1, 1) };

        Assert.Equal(new DateOnly(2026, 1, 1), couponLeg.StartDate);
        Assert.Equal(new DateOnly(2026, 7, 1), couponLeg.MaturityDate);
    }

    [Fact]
    public void CashFlowValuationRejectsNullInputs()
    {
        var referenceDate = new DateOnly(2026, 1, 1);
        var leg = new CashFlowLeg([new SimpleCashFlow(new DateOnly(2027, 1, 1), 100.0)]);
        var curve = new DiscountCurve(
        [
            new CurvePoint(0.0, 1.0),
            new CurvePoint(1.0, 0.95)
        ]);
        var yield = new InterestRate(0.05, CompoundingConvention.Continuous);

        Assert.Throws<ArgumentNullException>(() => CashFlowValuation.NetPresentValue(
            null!,
            curve,
            referenceDate,
            DayCountConvention.Actual365Fixed));
        Assert.Throws<ArgumentNullException>(() => CashFlowValuation.NetPresentValue(
            leg,
            null!,
            referenceDate,
            DayCountConvention.Actual365Fixed));
        Assert.Throws<ArgumentNullException>(() => CashFlowValuation.NetPresentValue(
            null!,
            yield,
            referenceDate,
            DayCountConvention.Actual365Fixed));
        Assert.Throws<ArgumentNullException>(() => CashFlowValuation.Yield(
            null!,
            95.0,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            CompoundingConvention.Continuous));
        Assert.Throws<ArgumentNullException>(() => CashFlowValuation.Duration(
            null!,
            yield,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            DurationType.Modified));
        Assert.Throws<ArgumentNullException>(() => CashFlowValuation.Convexity(
            null!,
            yield,
            referenceDate,
            DayCountConvention.Actual365Fixed));
        Assert.Throws<ArgumentNullException>(() => CashFlowValuation.BasisPointValue(
            null!,
            yield,
            referenceDate,
            DayCountConvention.Actual365Fixed));
    }

    [Fact]
    public void SettlementDateInclusionMatchesQuantLibReferenceDateSemantics()
    {
        var today = new DateOnly(2026, 1, 2);
        var leg = new CashFlowLeg(
        [
            new SimpleCashFlow(new DateOnly(2026, 1, 1), 1.0),
            new SimpleCashFlow(today, 1.0),
            new SimpleCashFlow(new DateOnly(2026, 1, 3), 1.0)
        ]);
        var noDiscount = new InterestRate(0.0, CompoundingConvention.Continuous);

        var excludeToday = CashFlowValuation.NetPresentValue(
            leg,
            noDiscount,
            referenceDate: today,
            DayCountConvention.Actual365Fixed,
            settlementDate: today,
            includeSettlementDateFlows: false);

        var includeToday = CashFlowValuation.NetPresentValue(
            leg,
            noDiscount,
            referenceDate: today,
            DayCountConvention.Actual365Fixed,
            settlementDate: today,
            includeSettlementDateFlows: true);

        Assert.Equal(1.0, excludeToday);
        Assert.Equal(2.0, includeToday);
    }

    [Fact]
    public void DiscountsCashFlowLegWithCurveAndNpvDateNormalization()
    {
        var referenceDate = new DateOnly(2026, 1, 1);
        var settlementDate = new DateOnly(2026, 7, 1);
        var curve = new DiscountCurve(
        [
            new CurvePoint(0.0, 1.0),
            new CurvePoint(181.0 / 365.0, 0.98),
            new CurvePoint(1.0, 0.95),
            new CurvePoint(2.0, 0.90)
        ]);
        var leg = new CashFlowLeg(
        [
            new SimpleCashFlow(new DateOnly(2026, 7, 1), 100.0),
            new SimpleCashFlow(new DateOnly(2027, 1, 1), 100.0),
            new SimpleCashFlow(new DateOnly(2028, 1, 1), 100.0)
        ]);

        var npv = CashFlowValuation.NetPresentValue(
            leg,
            curve,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate,
            npvDate: settlementDate);

        var expected = (100.0 * 0.95 + 100.0 * 0.90) / 0.98;
        AssertClose(expected, npv, 3e-3);
    }

    [Fact]
    public void CashFlowValuationRejectsNonfiniteDiscountedContribution()
    {
        var referenceDate = new DateOnly(2026, 1, 1);
        var leg = new CashFlowLeg(
        [
            new SimpleCashFlow(new DateOnly(2027, 1, 1), double.MaxValue)
        ]);
        var yield = new InterestRate(-0.75, CompoundingConvention.Continuous);

        Assert.Throws<ArgumentOutOfRangeException>(() => CashFlowValuation.NetPresentValue(
            leg,
            yield,
            referenceDate,
            DayCountConvention.Actual365Fixed));
    }

    [Fact]
    public void YieldDiscountedCashFlowValuationRejectsPaymentBeforeNpvDate()
    {
        var referenceDate = new DateOnly(2026, 1, 1);
        var settlementDate = new DateOnly(2026, 1, 1);
        var paymentDate = new DateOnly(2026, 7, 1);
        var leg = new CashFlowLeg([new SimpleCashFlow(paymentDate, 100.0)]);
        var yield = new InterestRate(0.05, CompoundingConvention.Continuous);

        Assert.Throws<ArgumentOutOfRangeException>(() => CashFlowValuation.NetPresentValue(
            leg,
            yield,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate,
            npvDate: paymentDate.AddDays(1)));
    }

    [Fact]
    public void BuildsFixedRateCashFlowsFromSchedule()
    {
        var schedule = ScheduleGenerator.Generate(ScheduleInput.Forward(
            new DateOnly(2026, 1, 1),
            new DateOnly(2027, 1, 1),
            ScheduleTenor.Semiannual,
            calendar: HolidayCalendar.WeekendsOnly,
            convention: BusinessDayConvention.Unadjusted));

        var coupons = FixedRateCashFlow.FromSchedule(
            schedule,
            nominal: 1_000_000.0,
            rate: 0.06,
            DayCountConvention.Actual365Fixed);

        Assert.Equal(2, coupons.Count);
        AssertClose(1_000_000.0 * 0.06 * (181.0 / 365.0), coupons[0].Amount, 1e-10);
        AssertClose(1_000_000.0 * 0.06 * (184.0 / 365.0), coupons[1].Amount, 1e-10);
    }

    [Fact]
    public void FixedRateCashFlowCanUseCompoundedCouponRate()
    {
        var coupon = new FixedRateCashFlow(
            PaymentDate: new DateOnly(2026, 7, 1),
            AccrualStartDate: new DateOnly(2026, 1, 1),
            AccrualEndDate: new DateOnly(2026, 7, 1),
            Nominal: 1_000_000.0,
            Rate: 0.06,
            AccrualYearFraction: 181.0 / 365.0,
            DayCountConvention: DayCountConvention.Actual365Fixed,
            Compounding: CompoundingConvention.Compounded,
            Frequency: 2);

        var expected = 1_000_000.0 * (Math.Pow(1.0 + 0.06 / 2.0, 2.0 * (181.0 / 365.0)) - 1.0);

        AssertClose(expected, coupon.Amount, 1e-10);
    }

    [Fact]
    public void FixedRateCashFlowCompoundedAccrualUsesPartialCompoundFactor()
    {
        var coupon = new FixedRateCashFlow(
            PaymentDate: new DateOnly(2026, 7, 1),
            AccrualStartDate: new DateOnly(2026, 1, 1),
            AccrualEndDate: new DateOnly(2026, 7, 1),
            Nominal: 1_000_000.0,
            Rate: 0.06,
            AccrualYearFraction: 181.0 / 365.0,
            DayCountConvention: DayCountConvention.Actual365Fixed,
            Compounding: CompoundingConvention.Compounded,
            Frequency: 2);

        var accrued = 90.0 / 365.0;
        var expected = 1_000_000.0 * (Math.Pow(1.0 + 0.06 / 2.0, 2.0 * accrued) - 1.0);

        AssertClose(expected, coupon.AccruedAmount(new DateOnly(2026, 4, 1)), 1e-10);
    }

    [Fact]
    public void FixedRateCashFlowAccruesInsideCouponPeriod()
    {
        var coupon = new FixedRateCashFlow(
            PaymentDate: new DateOnly(2026, 7, 1),
            AccrualStartDate: new DateOnly(2026, 1, 1),
            AccrualEndDate: new DateOnly(2026, 7, 1),
            Nominal: 1_000_000.0,
            Rate: 0.06,
            AccrualYearFraction: 181.0 / 365.0,
            DayCountConvention: DayCountConvention.Actual365Fixed);

        AssertClose(90.0 / 365.0, coupon.AccruedYearFraction(new DateOnly(2026, 4, 1)), 1e-15);
        Assert.Equal(90, coupon.AccruedDays(new DateOnly(2026, 4, 1)));
        AssertClose(1_000_000.0 * 0.06 * (90.0 / 365.0), coupon.AccruedAmount(new DateOnly(2026, 4, 1)), 1e-10);
        Assert.Equal(0.0, coupon.AccruedAmount(new DateOnly(2026, 1, 1)));
        Assert.Equal(0.0, coupon.AccruedAmount(new DateOnly(2026, 7, 2)));
    }

    [Fact]
    public void FixedRateCashFlowUsesNegativeAccrualWhenTradingExCoupon()
    {
        var coupon = new FixedRateCashFlow(
            PaymentDate: new DateOnly(2026, 7, 1),
            AccrualStartDate: new DateOnly(2026, 1, 1),
            AccrualEndDate: new DateOnly(2026, 7, 1),
            Nominal: 1_000_000.0,
            Rate: 0.06,
            AccrualYearFraction: 181.0 / 365.0,
            DayCountConvention: DayCountConvention.Actual365Fixed,
            ExCouponDate: new DateOnly(2026, 6, 29));

        Assert.False(coupon.TradingExCoupon(new DateOnly(2026, 6, 28)));
        Assert.True(coupon.TradingExCoupon(new DateOnly(2026, 6, 29)));
        AssertClose(-1_000_000.0 * 0.06 * (2.0 / 365.0), coupon.AccruedAmount(new DateOnly(2026, 6, 29)), 1e-10);
        AssertClose(-1_000_000.0 * 0.06 * (1.0 / 365.0), coupon.AccruedAmount(new DateOnly(2026, 6, 30)), 1e-10);
        Assert.Equal(0.0, coupon.AccruedAmount(new DateOnly(2026, 7, 1)));
    }

    [Fact]
    public void FixedRateCashFlowLegAccruesNextCouponLikeQuantLibCashFlows()
    {
        var coupons = new[]
        {
            new FixedRateCashFlow(
                PaymentDate: new DateOnly(2026, 7, 1),
                AccrualStartDate: new DateOnly(2026, 1, 1),
                AccrualEndDate: new DateOnly(2026, 7, 1),
                Nominal: 1_000_000.0,
                Rate: 0.05,
                AccrualYearFraction: 181.0 / 365.0,
                DayCountConvention: DayCountConvention.Actual365Fixed),
            new FixedRateCashFlow(
                PaymentDate: new DateOnly(2027, 1, 1),
                AccrualStartDate: new DateOnly(2026, 7, 1),
                AccrualEndDate: new DateOnly(2027, 1, 1),
                Nominal: 1_000_000.0,
                Rate: 0.05,
                AccrualYearFraction: 184.0 / 365.0,
                DayCountConvention: DayCountConvention.Actual365Fixed)
        };
        var leg = new FixedRateCashFlowLeg(coupons);

        Assert.Equal(new DateOnly(2026, 1, 1), leg.StartDate);
        Assert.Equal(new DateOnly(2027, 1, 1), leg.MaturityDate);
        AssertClose(1_000_000.0 * 0.05 * (90.0 / 365.0), leg.AccruedAmount(new DateOnly(2026, 4, 1)), 1e-10);
        AssertClose(1_000_000.0 * 0.05 * (92.0 / 365.0), leg.AccruedAmount(new DateOnly(2026, 10, 1)), 1e-10);
        Assert.Equal(0.0, leg.AccruedAmount(new DateOnly(2027, 1, 1)));
        AssertClose(coupons[1].Amount, leg.ToSimpleLeg().AmountOn(new DateOnly(2027, 1, 1)), 1e-10);
    }

    [Fact]
    public void FixedRateCashFlowScheduleCanAssignExCouponDates()
    {
        var schedule = ScheduleGenerator.Generate(ScheduleInput.Forward(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 7, 1),
            ScheduleTenor.Semiannual,
            calendar: HolidayCalendar.WeekendsOnly,
            convention: BusinessDayConvention.Unadjusted));

        var coupons = FixedRateCashFlow.FromSchedule(
            schedule,
            nominal: 1_000_000.0,
            rate: 0.06,
            DayCountConvention.Actual365Fixed,
            exCouponBusinessDays: 2,
            exCouponCalendar: HolidayCalendar.WeekendsOnly);

        Assert.Single(coupons);
        Assert.Equal(new DateOnly(2026, 6, 29), coupons[0].ExCouponDate);
    }

    [Fact]
    public void FixedRateCashFlowScheduleCanApplyPaymentLag()
    {
        var schedule = ScheduleGenerator.Generate(ScheduleInput.Forward(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 7, 1),
            ScheduleTenor.Semiannual,
            calendar: HolidayCalendar.WeekendsOnly,
            convention: BusinessDayConvention.Unadjusted));

        var coupons = FixedRateCashFlow.FromSchedule(
            schedule,
            nominal: 1_000_000.0,
            rate: 0.06,
            DayCountConvention.Actual365Fixed,
            paymentLagBusinessDays: 2);

        Assert.Single(coupons);
        Assert.Equal(new DateOnly(2026, 7, 3), coupons[0].PaymentDate);
        Assert.Equal(new DateOnly(2026, 7, 1), coupons[0].AccrualEndDate);
        AssertClose(1_000_000.0 * 0.06 * (181.0 / 365.0), coupons[0].Amount, 1e-10);
    }

    [Fact]
    public void FixedRateCashFlowExCouponDateUsesLaggedPaymentDate()
    {
        var schedule = ScheduleGenerator.Generate(ScheduleInput.Forward(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 7, 1),
            ScheduleTenor.Semiannual,
            calendar: HolidayCalendar.WeekendsOnly,
            convention: BusinessDayConvention.Unadjusted));

        var coupons = FixedRateCashFlow.FromSchedule(
            schedule,
            nominal: 1_000_000.0,
            rate: 0.06,
            DayCountConvention.Actual365Fixed,
            exCouponBusinessDays: 2,
            paymentLagBusinessDays: 2);

        Assert.Single(coupons);
        Assert.Equal(new DateOnly(2026, 7, 3), coupons[0].PaymentDate);
        Assert.Equal(new DateOnly(2026, 7, 1), coupons[0].ExCouponDate);
    }

    [Fact]
    public void AmortizingFixedRateBondRejectsNegativeExCouponDays()
    {
        var schedule = ScheduleGenerator.Generate(ScheduleInput.Forward(
            new DateOnly(2026, 1, 1),
            new DateOnly(2027, 1, 1),
            ScheduleTenor.Semiannual,
            calendar: HolidayCalendar.WeekendsOnly,
            convention: BusinessDayConvention.Unadjusted));

        Assert.Throws<ArgumentOutOfRangeException>(() => FixedRateBond.FromSchedule(
            [100.0, 100.0],
            schedule,
            couponRate: 0.05,
            DayCountConvention.Actual365Fixed,
            exCouponBusinessDays: -1));
    }

    [Fact]
    public void FindsPreviousAndNextCashFlowsExplicitly()
    {
        var settlement = new DateOnly(2026, 7, 1);
        var leg = new CashFlowLeg(
        [
            new SimpleCashFlow(new DateOnly(2026, 1, 1), 1.0),
            new SimpleCashFlow(settlement, 2.0),
            new SimpleCashFlow(new DateOnly(2027, 1, 1), 3.0)
        ]);

        Assert.Equal(2.0, leg.PreviousCashFlow(settlement)?.Amount);
        Assert.Equal(3.0, leg.NextCashFlow(settlement)?.Amount);
        Assert.Equal(2.0, leg.NextCashFlow(settlement, includeSettlementDateFlows: true)?.Amount);
        Assert.Equal(2.0, leg.AmountOn(settlement));
    }

    [Fact]
    public void SolvesContinuousYieldFromTargetNpv()
    {
        var referenceDate = new DateOnly(2026, 1, 1);
        var maturity = new DateOnly(2027, 1, 1);
        var leg = new CashFlowLeg([new SimpleCashFlow(maturity, 100.0)]);
        var targetNpv = 100.0 * Math.Exp(-0.05);

        var result = CashFlowValuation.Yield(
            leg,
            targetNpv,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            CompoundingConvention.Continuous,
            lower: -0.25,
            upper: 0.50);

        Assert.True(result.Converged);
        AssertClose(0.05, result.Yield, 1e-12);
        AssertClose(0.0, result.NpvResidual, 1e-10);
    }

    [Fact]
    public void YieldReportsNoBracketWhenCashFlowSignsCannotProduceTargetNpv()
    {
        var referenceDate = new DateOnly(2026, 1, 1);
        var leg = new CashFlowLeg([new SimpleCashFlow(new DateOnly(2027, 1, 1), 100.0)]);

        var negativeTarget = CashFlowValuation.Yield(
            leg,
            targetNpv: -1.0,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            CompoundingConvention.Continuous,
            lower: -0.25,
            upper: 0.50);

        Assert.False(negativeTarget.Converged);
        Assert.Equal(RootStatus.NoBracket, negativeTarget.Root.Status);
        Assert.True(double.IsNaN(negativeTarget.Yield));
        Assert.True(double.IsNaN(negativeTarget.NpvResidual));

        var zeroTarget = CashFlowValuation.Yield(
            leg,
            targetNpv: 0.0,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            CompoundingConvention.Continuous,
            lower: -0.25,
            upper: 0.50);

        Assert.False(zeroTarget.Converged);
        Assert.Equal(RootStatus.NoBracket, zeroTarget.Root.Status);
        Assert.True(double.IsNaN(zeroTarget.Yield));
        Assert.True(double.IsNaN(zeroTarget.NpvResidual));
    }

    [Fact]
    public void SimpleYieldBracketUsesPaymentTimeDomain()
    {
        var referenceDate = new DateOnly(2026, 1, 1);
        var maturity = referenceDate.AddYears(5);
        var leg = new CashFlowLeg([new SimpleCashFlow(maturity, 100.0)]);
        var time = DayCounts.YearFraction(referenceDate, maturity, DayCountConvention.Actual365Fixed).Value;
        var targetNpv = 100.0 / (1.0 + 0.05 * time);

        var result = CashFlowValuation.Yield(
            leg,
            targetNpv,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            CompoundingConvention.Simple,
            lower: -0.95,
            upper: 0.50);

        Assert.True(result.Converged);
        AssertClose(0.05, result.Yield, 1e-12);
    }

    [Fact]
    public void TransitionYieldBracketUsesPaymentTimeDomain()
    {
        var referenceDate = new DateOnly(2026, 1, 1);
        var leg = new CashFlowLeg(
        [
            new SimpleCashFlow(referenceDate.AddMonths(2), 10.0),
            new SimpleCashFlow(referenceDate.AddYears(5), 100.0)
        ]);
        var targetNpv = CashFlowValuation.NetPresentValue(
            leg,
            new InterestRate(0.05, CompoundingConvention.CompoundedThenSimple, Frequency: 4),
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate: referenceDate,
            npvDate: referenceDate);

        var result = CashFlowValuation.Yield(
            leg,
            targetNpv,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            CompoundingConvention.CompoundedThenSimple,
            frequency: 4,
            lower: -0.95,
            upper: 0.50);

        Assert.True(result.Converged);
        AssertClose(0.05, result.Yield, 1e-12);
    }

    [Fact]
    public void CashFlowYieldResultRejectsInconsistentStates()
    {
        var convergedRoot = new RootResult(true, 0.05, 0.0, 1, 3, -0.25, 0.50, RootStatus.Converged);
        var failedRoot = new RootResult(false, double.NaN, double.NaN, 0, 2, -0.25, 0.50, RootStatus.NoBracket);
        var invalidRoot = default(RootResult);

        Assert.Throws<ArgumentOutOfRangeException>(() => new CashFlowYieldResult(
            true, double.NaN, 0.0, convergedRoot));

        Assert.Throws<ArgumentOutOfRangeException>(() => new CashFlowYieldResult(
            true, 0.05, double.NaN, convergedRoot));

        Assert.Throws<ArgumentOutOfRangeException>(() => new CashFlowYieldResult(
            true, 0.05, 0.0, failedRoot));

        Assert.Throws<ArgumentOutOfRangeException>(() => new CashFlowYieldResult(
            false, double.NaN, double.NaN, convergedRoot));

        Assert.Throws<ArgumentOutOfRangeException>(() => new CashFlowYieldResult(
            false, 0.05, double.NaN, failedRoot));

        Assert.Throws<ArgumentOutOfRangeException>(() => new CashFlowYieldResult(
            false, double.NaN, 0.0, failedRoot));

        Assert.Throws<ArgumentOutOfRangeException>(() => new CashFlowYieldResult(
            false, double.NaN, double.NaN, invalidRoot));
    }

    [Fact]
    public void DurationAndConvexityMatchContinuousZeroCouponIdentities()
    {
        var referenceDate = new DateOnly(2026, 1, 1);
        var maturity = new DateOnly(2028, 1, 1);
        var leg = new CashFlowLeg([new SimpleCashFlow(maturity, 100.0)]);
        var yield = new InterestRate(0.05, CompoundingConvention.Continuous);
        var time = DayCounts.YearFraction(referenceDate, maturity, DayCountConvention.Actual365Fixed).Value;

        var simpleDuration = CashFlowValuation.Duration(
            leg,
            yield,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            DurationType.Simple);
        var modifiedDuration = CashFlowValuation.Duration(
            leg,
            yield,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            DurationType.Modified);
        var convexity = CashFlowValuation.Convexity(
            leg,
            yield,
            referenceDate,
            DayCountConvention.Actual365Fixed);

        AssertClose(time, simpleDuration, 1e-15);
        AssertClose(time, modifiedDuration, 1e-15);
        AssertClose(time * time, convexity, 1e-15);
    }

    [Fact]
    public void MacaulayDurationRequiresCompoundedYield()
    {
        var referenceDate = new DateOnly(2026, 1, 1);
        var leg = new CashFlowLeg([new SimpleCashFlow(new DateOnly(2027, 1, 1), 100.0)]);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CashFlowValuation.Duration(
                leg,
                new InterestRate(0.05, CompoundingConvention.Continuous),
                referenceDate,
                DayCountConvention.Actual365Fixed,
                DurationType.Macaulay));
    }

    [Fact]
    public void DurationRejectsNonfiniteDerivativeContribution()
    {
        var referenceDate = new DateOnly(2026, 1, 1);
        var leg = new CashFlowLeg([new SimpleCashFlow(new DateOnly(2027, 1, 1), 1e277)]);
        var yield = new InterestRate(-0.9999999999999999, CompoundingConvention.Simple);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CashFlowValuation.Duration(
                leg,
                yield,
                referenceDate,
                DayCountConvention.Actual365Fixed,
                DurationType.Modified));
    }

    [Fact]
    public void ConvexityRejectsNonfiniteSecondDerivativeContribution()
    {
        var referenceDate = new DateOnly(2026, 1, 1);
        var leg = new CashFlowLeg([new SimpleCashFlow(new DateOnly(2027, 1, 1), 1e270)]);
        var yield = new InterestRate(-0.9999999999999999, CompoundingConvention.Simple);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CashFlowValuation.Convexity(
                leg,
                yield,
                referenceDate,
                DayCountConvention.Actual365Fixed));
    }

    [Fact]
    public void BasisPointValueMatchesFiniteDifferenceDirection()
    {
        var referenceDate = new DateOnly(2026, 1, 1);
        var leg = new CashFlowLeg(
        [
            new SimpleCashFlow(new DateOnly(2027, 1, 1), 5.0),
            new SimpleCashFlow(new DateOnly(2028, 1, 1), 105.0)
        ]);
        var yield = new InterestRate(0.04, CompoundingConvention.Continuous);

        var approximation = CashFlowValuation.BasisPointValue(
            leg,
            yield,
            referenceDate,
            DayCountConvention.Actual365Fixed);
        var baseNpv = CashFlowValuation.NetPresentValue(
            leg,
            yield,
            referenceDate,
            DayCountConvention.Actual365Fixed);
        var bumpedNpv = CashFlowValuation.NetPresentValue(
            leg,
            new InterestRate(0.0401, CompoundingConvention.Continuous),
            referenceDate,
            DayCountConvention.Actual365Fixed);

        AssertClose(bumpedNpv - baseNpv, approximation, 1e-7);
    }

    [Fact]
    public void FixedRateBondDirtyPriceScalesSettlementValueByCurrentNotional()
    {
        var settlementDate = new DateOnly(2026, 1, 1);
        var bond = CreateTwoYearFixedRateBond();
        var yield = new InterestRate(0.05, CompoundingConvention.Continuous);

        var dirtyPrice = BondValuation.DirtyPrice(
            bond,
            yield,
            referenceDate: settlementDate,
            dayCountConvention: DayCountConvention.Actual365Fixed,
            settlementDate: settlementDate);

        var settlementValue = CashFlowValuation.NetPresentValue(
            bond.CashFlows(),
            yield,
            referenceDate: settlementDate,
            dayCountConvention: DayCountConvention.Actual365Fixed,
            settlementDate: settlementDate,
            npvDate: settlementDate);

        AssertClose(settlementValue * 100.0 / bond.FaceAmount, dirtyPrice, 1e-10);
    }

    [Fact]
    public void BondValuationRejectsNullInputs()
    {
        var settlementDate = new DateOnly(2026, 1, 1);
        var bond = CreateTwoYearFixedRateBond();
        var curve = new DiscountCurve(
        [
            new CurvePoint(0.0, 1.0),
            new CurvePoint(1.0, 0.95),
            new CurvePoint(2.0, 0.90)
        ]);
        var yield = new InterestRate(0.05, CompoundingConvention.Continuous);

        Assert.Throws<ArgumentNullException>(() => BondValuation.DirtyPrice(
            null!,
            curve,
            settlementDate,
            DayCountConvention.Actual365Fixed,
            settlementDate));
        Assert.Throws<ArgumentNullException>(() => BondValuation.DirtyPrice(
            bond,
            null!,
            settlementDate,
            DayCountConvention.Actual365Fixed,
            settlementDate));
        Assert.Throws<ArgumentNullException>(() => BondValuation.DirtyPrice(
            null!,
            yield,
            settlementDate,
            DayCountConvention.Actual365Fixed,
            settlementDate));
        Assert.Throws<ArgumentNullException>(() => BondValuation.DirtyPriceWithSpread(
            null!,
            curve,
            0.001,
            CompoundingConvention.Continuous,
            frequency: 1,
            settlementDate,
            DayCountConvention.Actual365Fixed,
            settlementDate));
        Assert.Throws<ArgumentNullException>(() => BondValuation.DirtyPriceWithSpread(
            bond,
            null!,
            0.001,
            CompoundingConvention.Continuous,
            frequency: 1,
            settlementDate,
            DayCountConvention.Actual365Fixed,
            settlementDate));
        Assert.Throws<ArgumentNullException>(() => BondValuation.SettlementValue(null!, 100.0, settlementDate));
        Assert.Throws<ArgumentNullException>(() => BondValuation.Duration(
            null!,
            yield,
            settlementDate,
            DayCountConvention.Actual365Fixed,
            DurationType.Modified,
            settlementDate));
        Assert.Throws<ArgumentNullException>(() => BondValuation.CouponBasisPointValue(
            bond,
            null!,
            settlementDate,
            DayCountConvention.Actual365Fixed,
            settlementDate));
        Assert.Throws<ArgumentNullException>(() => BondValuation.ParCouponRate(
            bond,
            100.0,
            BondPriceType.Clean,
            null!,
            settlementDate,
            DayCountConvention.Actual365Fixed,
            settlementDate));
        Assert.Throws<ArgumentNullException>(() => BondValuation.ZSpread(
            bond,
            100.0,
            BondPriceType.Clean,
            null!,
            CompoundingConvention.Continuous,
            frequency: 1,
            settlementDate,
            DayCountConvention.Actual365Fixed,
            settlementDate));
    }

    [Fact]
    public void FixedRateBondCleanPriceIsDirtyMinusAccrued()
    {
        var settlementDate = new DateOnly(2026, 4, 1);
        var bond = CreateTwoYearFixedRateBond();
        var yield = new InterestRate(0.05, CompoundingConvention.Continuous);

        var dirtyPrice = BondValuation.DirtyPrice(
            bond,
            yield,
            referenceDate: settlementDate,
            dayCountConvention: DayCountConvention.Actual365Fixed,
            settlementDate: settlementDate);
        var cleanPrice = BondValuation.CleanPrice(
            bond,
            yield,
            referenceDate: settlementDate,
            dayCountConvention: DayCountConvention.Actual365Fixed,
            settlementDate: settlementDate);

        AssertClose(dirtyPrice - bond.AccruedAmount(settlementDate), cleanPrice, 1e-12);
        AssertClose(1_000_000.0 * 0.05 * (90.0 / 365.0) * 100.0 / bond.FaceAmount, bond.AccruedAmount(settlementDate), 1e-10);
    }

    [Fact]
    public void FixedRateBondSettlementValueConvertsCleanPriceBackToCurrencyAmount()
    {
        var settlementDate = new DateOnly(2026, 4, 1);
        var bond = CreateTwoYearFixedRateBond();
        var cleanPrice = 101.25;

        var settlementValue = BondValuation.SettlementValue(bond, cleanPrice, settlementDate);

        AssertClose((cleanPrice + bond.AccruedAmount(settlementDate)) / 100.0 * bond.FaceAmount, settlementValue, 1e-10);
    }

    [Fact]
    public void FixedRateBondYieldRoundTripsFromCleanPrice()
    {
        var settlementDate = new DateOnly(2026, 4, 1);
        var bond = CreateTwoYearFixedRateBond();
        var yield = new InterestRate(0.0475, CompoundingConvention.Compounded, Frequency: 2);
        var cleanPrice = BondValuation.CleanPrice(
            bond,
            yield,
            referenceDate: settlementDate,
            dayCountConvention: DayCountConvention.Actual365Fixed,
            settlementDate: settlementDate);

        var solved = BondValuation.Yield(
            bond,
            cleanPrice,
            BondPriceType.Clean,
            referenceDate: settlementDate,
            dayCountConvention: DayCountConvention.Actual365Fixed,
            compounding: CompoundingConvention.Compounded,
            frequency: 2,
            settlementDate: settlementDate,
            lower: -0.25,
            upper: 0.25);

        Assert.True(solved.Converged);
        AssertClose(0.0475, solved.Yield, 1e-10);
    }

    [Fact]
    public void FixedRateBondZeroSpreadDirtyPriceMatchesCurveDirtyPrice()
    {
        var referenceDate = new DateOnly(2026, 1, 1);
        var settlementDate = new DateOnly(2026, 4, 1);
        var bond = CreateTwoYearFixedRateBond();
        var curve = CreateBondDiscountCurve();

        var curveDirtyPrice = BondValuation.DirtyPrice(
            bond,
            curve,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate);
        var zeroSpreadDirtyPrice = BondValuation.DirtyPriceWithContinuousSpread(
            bond,
            curve,
            continuousSpread: 0.0,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate);

        AssertClose(curveDirtyPrice, zeroSpreadDirtyPrice, 1e-12);
    }

    [Fact]
    public void FixedRateBondContinuousSpreadMatchesGenericContinuousSpread()
    {
        var referenceDate = new DateOnly(2026, 1, 1);
        var settlementDate = new DateOnly(2026, 4, 1);
        var bond = CreateTwoYearFixedRateBond();
        var curve = CreateBondDiscountCurve();

        var continuousPrice = BondValuation.DirtyPriceWithContinuousSpread(
            bond,
            curve,
            continuousSpread: 0.0125,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate);
        var genericPrice = BondValuation.DirtyPriceWithSpread(
            bond,
            curve,
            spread: 0.0125,
            CompoundingConvention.Continuous,
            frequency: 1,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate);

        AssertClose(continuousPrice, genericPrice, 1e-12);
    }

    [Fact]
    public void FixedRateBondContinuousSpreadLowersPriceWhenPositive()
    {
        var referenceDate = new DateOnly(2026, 1, 1);
        var settlementDate = new DateOnly(2026, 4, 1);
        var bond = CreateTwoYearFixedRateBond();
        var curve = CreateBondDiscountCurve();

        var zeroSpreadPrice = BondValuation.DirtyPriceWithContinuousSpread(
            bond,
            curve,
            continuousSpread: 0.0,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate);
        var positiveSpreadPrice = BondValuation.DirtyPriceWithContinuousSpread(
            bond,
            curve,
            continuousSpread: 0.01,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate);

        Assert.True(positiveSpreadPrice < zeroSpreadPrice);
    }

    [Fact]
    public void FixedRateBondCompoundedSpreadLowersPriceWhenPositive()
    {
        var referenceDate = new DateOnly(2026, 1, 1);
        var settlementDate = new DateOnly(2026, 4, 1);
        var bond = CreateTwoYearFixedRateBond();
        var curve = CreateBondDiscountCurve();

        var zeroSpreadPrice = BondValuation.DirtyPriceWithSpread(
            bond,
            curve,
            spread: 0.0,
            CompoundingConvention.Compounded,
            frequency: 2,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate);
        var positiveSpreadPrice = BondValuation.DirtyPriceWithSpread(
            bond,
            curve,
            spread: 0.01,
            CompoundingConvention.Compounded,
            frequency: 2,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate);

        Assert.True(positiveSpreadPrice < zeroSpreadPrice);
    }

    [Fact]
    public void FixedRateBondDirtyPriceRejectsNonfinitePerHundredScaling()
    {
        var settlementDate = new DateOnly(2026, 1, 1);
        var bond = CreateTinyNotionalHugeCouponBond();
        var yield = new InterestRate(0.0, CompoundingConvention.Continuous);

        Assert.Throws<ArgumentOutOfRangeException>(() => BondValuation.DirtyPrice(
            bond,
            yield,
            referenceDate: settlementDate,
            DayCountConvention.Actual365Fixed,
            settlementDate));
    }

    [Fact]
    public void FixedRateBondYieldRejectsNonfiniteCleanToDirtyProjection()
    {
        var settlementDate = new DateOnly(2026, 7, 1);
        var bond = CreateTinyNotionalHugeCouponBond();

        Assert.Throws<ArgumentOutOfRangeException>(() => BondValuation.Yield(
            bond,
            price: 100.0,
            BondPriceType.Clean,
            referenceDate: settlementDate,
            dayCountConvention: DayCountConvention.Actual365Fixed,
            CompoundingConvention.Continuous,
            frequency: 1,
            settlementDate));
    }

    [Fact]
    public void FixedRateBondZSpreadRejectsNonfiniteCleanToDirtyProjection()
    {
        var settlementDate = new DateOnly(2026, 7, 1);
        var bond = CreateTinyNotionalHugeCouponBond();
        var curve = new DiscountCurve(
        [
            new CurvePoint(0.0, 1.0),
            new CurvePoint(1.0, 1.0)
        ]);

        Assert.Throws<ArgumentOutOfRangeException>(() => BondValuation.ZSpread(
            bond,
            price: 100.0,
            BondPriceType.Clean,
            curve,
            CompoundingConvention.Continuous,
            frequency: 1,
            referenceDate: settlementDate,
            dayCountConvention: DayCountConvention.Actual365Fixed,
            settlementDate));
    }

    [Fact]
    public void FixedRateBondSpreadDirtyPriceRejectsNonfiniteContribution()
    {
        var referenceDate = new DateOnly(2026, 1, 1);
        var settlementDate = referenceDate;
        var bond = CreateTwoYearFixedRateBond();
        var curve = new DiscountCurve(
        [
            new CurvePoint(0.0, 1.0),
            new CurvePoint(1.0, double.MaxValue),
            new CurvePoint(2.0, double.MaxValue)
        ]);

        Assert.Throws<ArgumentOutOfRangeException>(() => BondValuation.DirtyPriceWithSpread(
            bond,
            curve,
            spread: 0.0,
            CompoundingConvention.Continuous,
            frequency: 1,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate));
    }

    [Fact]
    public void FixedRateBondContinuousZSpreadRoundTripsFromCleanPrice()
    {
        var referenceDate = new DateOnly(2026, 1, 1);
        var settlementDate = new DateOnly(2026, 4, 1);
        var bond = CreateTwoYearFixedRateBond();
        var curve = CreateBondDiscountCurve();
        var cleanPrice = BondValuation.CleanPriceWithContinuousSpread(
            bond,
            curve,
            continuousSpread: 0.0125,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate);

        var solved = BondValuation.ContinuousZSpread(
            bond,
            cleanPrice,
            BondPriceType.Clean,
            curve,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate,
            lower: -0.05,
            upper: 0.05);

        Assert.True(solved.Converged);
        AssertClose(0.0125, solved.Spread, 1e-10);
        AssertClose(0.0, solved.PriceResidual, 1e-10);
    }

    [Fact]
    public void FixedRateBondCompoundedZSpreadRoundTripsFromCleanPrice()
    {
        var referenceDate = new DateOnly(2026, 1, 1);
        var settlementDate = new DateOnly(2026, 4, 1);
        var bond = CreateTwoYearFixedRateBond();
        var curve = CreateBondDiscountCurve();
        var cleanPrice = BondValuation.CleanPriceWithSpread(
            bond,
            curve,
            spread: 0.0125,
            CompoundingConvention.Compounded,
            frequency: 2,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate);

        var solved = BondValuation.ZSpread(
            bond,
            cleanPrice,
            BondPriceType.Clean,
            curve,
            CompoundingConvention.Compounded,
            frequency: 2,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate,
            lower: -0.05,
            upper: 0.05);

        Assert.True(solved.Converged);
        AssertClose(0.0125, solved.Spread, 1e-10);
        AssertClose(0.0, solved.PriceResidual, 1e-10);
    }

    [Fact]
    public void BondSpreadResultRejectsInconsistentStates()
    {
        var convergedRoot = new RootResult(true, 0.0125, 0.0, 1, 3, -0.05, 0.05, RootStatus.Converged);
        var failedRoot = new RootResult(false, double.NaN, double.NaN, 0, 2, -0.05, 0.05, RootStatus.NoBracket);
        var invalidRoot = default(RootResult);

        Assert.Throws<ArgumentOutOfRangeException>(() => new BondSpreadResult(
            true, double.NaN, 0.0, convergedRoot));

        Assert.Throws<ArgumentOutOfRangeException>(() => new BondSpreadResult(
            true, 0.0125, double.NaN, convergedRoot));

        Assert.Throws<ArgumentOutOfRangeException>(() => new BondSpreadResult(
            true, 0.0125, 0.0, failedRoot));

        Assert.Throws<ArgumentOutOfRangeException>(() => new BondSpreadResult(
            false, double.NaN, double.NaN, convergedRoot));

        Assert.Throws<ArgumentOutOfRangeException>(() => new BondSpreadResult(
            false, 0.0125, double.NaN, failedRoot));

        Assert.Throws<ArgumentOutOfRangeException>(() => new BondSpreadResult(
            false, double.NaN, 0.0, failedRoot));

        Assert.Throws<ArgumentOutOfRangeException>(() => new BondSpreadResult(
            false, double.NaN, double.NaN, invalidRoot));
    }

    [Fact]
    public void FixedRateBondDurationAndConvexityMatchUnderlyingCashFlows()
    {
        var settlementDate = new DateOnly(2026, 4, 1);
        var bond = CreateTwoYearFixedRateBond();
        var yield = new InterestRate(0.0475, CompoundingConvention.Compounded, Frequency: 2);

        var bondDuration = BondValuation.Duration(
            bond,
            yield,
            referenceDate: settlementDate,
            dayCountConvention: DayCountConvention.Actual365Fixed,
            DurationType.Modified,
            settlementDate);
        var cashFlowDuration = CashFlowValuation.Duration(
            bond.CashFlows(),
            yield,
            referenceDate: settlementDate,
            DayCountConvention.Actual365Fixed,
            DurationType.Modified,
            settlementDate,
            npvDate: settlementDate);
        var bondConvexity = BondValuation.Convexity(
            bond,
            yield,
            referenceDate: settlementDate,
            DayCountConvention.Actual365Fixed,
            settlementDate);
        var cashFlowConvexity = CashFlowValuation.Convexity(
            bond.CashFlows(),
            yield,
            referenceDate: settlementDate,
            DayCountConvention.Actual365Fixed,
            settlementDate,
            npvDate: settlementDate);

        AssertClose(cashFlowDuration, bondDuration, 1e-15);
        AssertClose(cashFlowConvexity, bondConvexity, 1e-15);
    }

    [Fact]
    public void FixedRateBondBasisPointValueIsNormalizedPerHundredNotional()
    {
        var settlementDate = new DateOnly(2026, 4, 1);
        var bond = CreateTwoYearFixedRateBond();
        var yield = new InterestRate(0.0475, CompoundingConvention.Continuous);

        var bondBpv = BondValuation.BasisPointValue(
            bond,
            yield,
            referenceDate: settlementDate,
            DayCountConvention.Actual365Fixed,
            settlementDate);
        var cashFlowBpv = CashFlowValuation.BasisPointValue(
            bond.CashFlows(),
            yield,
            referenceDate: settlementDate,
            DayCountConvention.Actual365Fixed,
            settlementDate,
            npvDate: settlementDate);

        AssertClose(cashFlowBpv * 100.0 / bond.FaceAmount, bondBpv, 1e-12);
    }

    [Fact]
    public void FixedRateBondYieldValueBasisPointInvertsPriceBpv()
    {
        var settlementDate = new DateOnly(2026, 4, 1);
        var bond = CreateTwoYearFixedRateBond();
        var yield = new InterestRate(0.0475, CompoundingConvention.Continuous);

        var priceBpv = BondValuation.BasisPointValue(
            bond,
            yield,
            referenceDate: settlementDate,
            DayCountConvention.Actual365Fixed,
            settlementDate);
        var yieldValue = BondValuation.YieldValueBasisPoint(
            bond,
            yield,
            referenceDate: settlementDate,
            DayCountConvention.Actual365Fixed,
            settlementDate);

        Assert.True(yieldValue > 0.0);
        AssertClose(1e-4, yieldValue * -priceBpv, 1e-15);
    }

    [Theory]
    [InlineData(CompoundingConvention.SimpleThenCompounded)]
    [InlineData(CompoundingConvention.CompoundedThenSimple)]
    public void TransitionCompoundingDurationMatchesFiniteDifferenceDerivative(CompoundingConvention compounding)
    {
        var referenceDate = new DateOnly(2026, 1, 1);
        var leg = new CashFlowLeg(
        [
            new SimpleCashFlow(referenceDate.AddDays(73), 100.0),
            new SimpleCashFlow(referenceDate.AddDays(365), 100.0)
        ]);
        var yield = new InterestRate(0.05, compounding, Frequency: 4);
        var duration = CashFlowValuation.Duration(
            leg,
            yield,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            DurationType.Modified,
            settlementDate: referenceDate,
            npvDate: referenceDate);
        var npv = CashFlowValuation.NetPresentValue(
            leg,
            yield,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate: referenceDate,
            npvDate: referenceDate);
        var finiteDifferenceDerivative = CentralNpvDerivative(leg, referenceDate, compounding, frequency: 4, rate: 0.05);

        AssertClose(-finiteDifferenceDerivative / npv, duration, 1e-7);
    }

    [Theory]
    [InlineData(CompoundingConvention.SimpleThenCompounded)]
    [InlineData(CompoundingConvention.CompoundedThenSimple)]
    public void TransitionCompoundingConvexityMatchesFiniteDifferenceSecondDerivative(CompoundingConvention compounding)
    {
        var referenceDate = new DateOnly(2026, 1, 1);
        var leg = new CashFlowLeg(
        [
            new SimpleCashFlow(referenceDate.AddDays(73), 100.0),
            new SimpleCashFlow(referenceDate.AddDays(365), 100.0)
        ]);
        var yield = new InterestRate(0.05, compounding, Frequency: 4);
        var convexity = CashFlowValuation.Convexity(
            leg,
            yield,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate: referenceDate,
            npvDate: referenceDate);
        var npv = CashFlowValuation.NetPresentValue(
            leg,
            yield,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate: referenceDate,
            npvDate: referenceDate);
        var finiteDifferenceSecondDerivative = CentralNpvSecondDerivative(leg, referenceDate, compounding, frequency: 4, rate: 0.05);

        AssertClose(finiteDifferenceSecondDerivative / npv, convexity, 1e-5);
    }

    [Fact]
    public void FixedRateBondCouponBasisPointValueUsesOnlyActiveCoupons()
    {
        var settlementDate = new DateOnly(2026, 4, 1);
        var bond = CreateTwoYearFixedRateBond();
        var curve = CreateBondDiscountCurve();

        var bps = BondValuation.CouponBasisPointValue(
            bond,
            curve,
            referenceDate: new DateOnly(2026, 1, 1),
            DayCountConvention.Actual365Fixed,
            settlementDate);

        var settlementDiscount = curve.DiscountFactor(90.0 / 365.0);
        var expected = bond.Coupons.Coupons
            .Where(coupon => coupon.PaymentDate > settlementDate)
            .Sum(coupon =>
            {
                var paymentTime = DayCounts.YearFraction(new DateOnly(2026, 1, 1), coupon.PaymentDate, DayCountConvention.Actual365Fixed).Value;
                return coupon.Nominal * coupon.AccrualYearFraction * curve.DiscountFactor(paymentTime) / settlementDiscount;
            }) * 1e-4 * 100.0 / bond.FaceAmount;

        AssertClose(expected, bps, 1e-12);
    }

    [Fact]
    public void FixedRateBondCouponBasisPointValueDiffersFromYieldPriceBpv()
    {
        var settlementDate = new DateOnly(2026, 4, 1);
        var bond = CreateTwoYearFixedRateBond();
        var yield = new InterestRate(0.0475, CompoundingConvention.Continuous);

        var couponBps = BondValuation.CouponBasisPointValue(
            bond,
            yield,
            referenceDate: settlementDate,
            DayCountConvention.Actual365Fixed,
            settlementDate);
        var priceBpv = BondValuation.BasisPointValue(
            bond,
            yield,
            referenceDate: settlementDate,
            DayCountConvention.Actual365Fixed,
            settlementDate);

        Assert.True(couponBps > 0.0);
        Assert.True(priceBpv < 0.0);
    }

    [Fact]
    public void FixedRateBondCouponAnnuityMatchesCouponBpsScaling()
    {
        var referenceDate = new DateOnly(2026, 1, 1);
        var settlementDate = new DateOnly(2026, 4, 1);
        var bond = CreateTwoYearFixedRateBond();
        var curve = CreateBondDiscountCurve();

        var annuity = BondValuation.CouponAnnuity(
            bond,
            curve,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate);
        var couponBps = BondValuation.CouponBasisPointValue(
            bond,
            curve,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate);

        AssertClose(annuity * 1e-4 * 100.0 / bond.FaceAmount, couponBps, 1e-12);
    }

    [Fact]
    public void FixedRateBondCouponBasisPointValueUsesCompoundedCouponDerivative()
    {
        var referenceDate = new DateOnly(2026, 1, 1);
        var settlementDate = new DateOnly(2026, 4, 1);
        var curve = CreateBondDiscountCurve();
        var bond = CreateTwoYearCompoundedCouponBond(couponRate: 0.05);

        var couponBps = BondValuation.CouponBasisPointValue(
            bond,
            curve,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate);
        var finiteDifferencePriceDerivative = CentralDirtyPriceCouponDerivative(
            couponRate => CreateTwoYearCompoundedCouponBond(couponRate),
            curve,
            referenceDate,
            settlementDate,
            rate: 0.05);

        AssertClose(finiteDifferencePriceDerivative * 1e-4, couponBps, 1e-7);
    }

    [Fact]
    public void FixedRateBondCouponBasisPointValueRejectsNonfiniteCouponDerivativeAmount()
    {
        var referenceDate = new DateOnly(2026, 1, 1);
        var settlementDate = referenceDate;
        var bond = CreateExtremeContinuousCouponBond();
        var curve = new DiscountCurve(
        [
            new CurvePoint(0.0, 1.0),
            new CurvePoint(1.0, 1.0)
        ]);
        var yield = new InterestRate(0.0, CompoundingConvention.Continuous);

        Assert.Throws<ArgumentOutOfRangeException>(() => BondValuation.CouponBasisPointValue(
            bond,
            curve,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate));

        Assert.Throws<ArgumentOutOfRangeException>(() => BondValuation.CouponBasisPointValue(
            bond,
            yield,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate));
    }

    [Fact]
    public void FixedRateBondCouponAnnuityRejectsNonfiniteCouponDerivativeAmount()
    {
        var referenceDate = new DateOnly(2026, 1, 1);
        var bond = CreateExtremeContinuousCouponBond();
        var curve = new DiscountCurve(
        [
            new CurvePoint(0.0, 1.0),
            new CurvePoint(1.0, 1.0)
        ]);

        Assert.Throws<ArgumentOutOfRangeException>(() => BondValuation.CouponAnnuity(
            bond,
            curve,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate: referenceDate));
    }

    [Fact]
    public void FixedRateBondParCouponRatePricesBondToTargetCleanPrice()
    {
        var referenceDate = new DateOnly(2026, 1, 1);
        var settlementDate = new DateOnly(2026, 4, 1);
        var curve = CreateBondDiscountCurve();
        var schedule = CreateTwoYearSemiannualSchedule();
        var templateBond = FixedRateBond.FromSchedule(
            faceAmount: 1_000_000.0,
            schedule,
            couponRate: 0.01,
            DayCountConvention.Actual365Fixed,
            issueDate: new DateOnly(2026, 1, 1));

        var parRate = BondValuation.ParCouponRate(
            templateBond,
            price: 100.0,
            BondPriceType.Clean,
            curve,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate);
        var parBond = FixedRateBond.FromSchedule(
            faceAmount: 1_000_000.0,
            schedule,
            couponRate: parRate,
            DayCountConvention.Actual365Fixed,
            issueDate: new DateOnly(2026, 1, 1));

        var cleanPrice = BondValuation.CleanPrice(
            parBond,
            curve,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate);

        AssertClose(100.0, cleanPrice, 1e-10);
    }

    [Fact]
    public void FixedRateBondParCouponRateSolvesCompoundedCouponBond()
    {
        var referenceDate = new DateOnly(2026, 1, 1);
        var settlementDate = new DateOnly(2026, 4, 1);
        var curve = CreateBondDiscountCurve();
        var templateBond = CreateTwoYearCompoundedCouponBond(couponRate: 0.01);

        var parRate = BondValuation.ParCouponRate(
            templateBond,
            price: 100.0,
            BondPriceType.Clean,
            curve,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate);
        var parBond = CreateTwoYearCompoundedCouponBond(parRate);
        var cleanPrice = BondValuation.CleanPrice(
            parBond,
            curve,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate);

        AssertClose(100.0, cleanPrice, 1e-10);
    }

    [Fact]
    public void FixedRateBondDirtyPriceSkipsCouponTradingExCoupon()
    {
        var settlementDate = new DateOnly(2026, 6, 29);
        var bond = CreateTwoYearExCouponFixedRateBond();
        var yield = new InterestRate(0.0, CompoundingConvention.Continuous);

        Assert.True(bond.Coupons.Coupons[0].TradingExCoupon(settlementDate));

        var dirtyPrice = BondValuation.DirtyPrice(
            bond,
            yield,
            referenceDate: settlementDate,
            DayCountConvention.Actual365Fixed,
            settlementDate);
        var expectedSettlementValue = bond.Coupons.Coupons
            .Skip(1)
            .Sum(coupon => coupon.Amount)
            + bond.FaceAmount * bond.RedemptionAmount / 100.0;

        AssertClose(expectedSettlementValue * 100.0 / bond.FaceAmount, dirtyPrice, 1e-10);
    }

    [Fact]
    public void FixedRateBondYieldRoundTripsWhenCouponTradesExCoupon()
    {
        var settlementDate = new DateOnly(2026, 6, 29);
        var bond = CreateTwoYearExCouponFixedRateBond();
        var yield = new InterestRate(0.043, CompoundingConvention.Compounded, Frequency: 2);
        var cleanPrice = BondValuation.CleanPrice(
            bond,
            yield,
            referenceDate: settlementDate,
            DayCountConvention.Actual365Fixed,
            settlementDate);

        var solved = BondValuation.Yield(
            bond,
            cleanPrice,
            BondPriceType.Clean,
            referenceDate: settlementDate,
            DayCountConvention.Actual365Fixed,
            CompoundingConvention.Compounded,
            frequency: 2,
            settlementDate,
            lower: -0.25,
            upper: 0.25);

        Assert.True(solved.Converged);
        AssertClose(0.043, solved.Yield, 1e-10);
    }

    [Fact]
    public void FixedRateBondDerivesRedemptionsFromAmortizingNotionals()
    {
        var bond = CreateAmortizingFixedRateBond();
        var redemptions = bond.RedemptionCashFlows();

        Assert.Equal(4, redemptions.Count);
        AssertClose(200_000.0, redemptions[0].Amount, 1e-10);
        AssertClose(200_000.0, redemptions[1].Amount, 1e-10);
        AssertClose(200_000.0, redemptions[2].Amount, 1e-10);
        AssertClose(400_000.0, redemptions[3].Amount, 1e-10);
        Assert.Equal(new DateOnly(2026, 7, 1), redemptions[0].PaymentDate);
        Assert.Equal(new DateOnly(2028, 1, 1), redemptions[^1].PaymentDate);
    }

    [Fact]
    public void FixedRateBondCurrentNotionalStepsDownAfterRedemptionDates()
    {
        var bond = CreateAmortizingFixedRateBond();

        AssertClose(1_000_000.0, bond.CurrentNotional(new DateOnly(2026, 6, 30)), 1e-10);
        AssertClose(800_000.0, bond.CurrentNotional(new DateOnly(2026, 7, 1)), 1e-10);
        AssertClose(600_000.0, bond.CurrentNotional(new DateOnly(2027, 1, 1)), 1e-10);
        AssertClose(400_000.0, bond.CurrentNotional(new DateOnly(2027, 7, 1)), 1e-10);
        AssertClose(0.0, bond.CurrentNotional(new DateOnly(2028, 1, 1)), 1e-10);
    }

    [Fact]
    public void FixedRateBondDirtyPriceUsesCurrentNotionalForAmortizingBond()
    {
        var settlementDate = new DateOnly(2026, 7, 1);
        var bond = CreateAmortizingFixedRateBond();
        var yield = new InterestRate(0.0, CompoundingConvention.Continuous);

        var dirtyPrice = BondValuation.DirtyPrice(
            bond,
            yield,
            referenceDate: settlementDate,
            DayCountConvention.Actual365Fixed,
            settlementDate);
        var expectedSettlementValue = bond.CashFlows().Flows
            .Where(flow => flow.PaymentDate > settlementDate)
            .Sum(flow => flow.Amount);

        AssertClose(expectedSettlementValue * 100.0 / bond.CurrentNotional(settlementDate), dirtyPrice, 1e-10);
    }

    [Fact]
    public void FixedRateBondPaymentLagMovesCouponsAndRedemptionsTogether()
    {
        var bond = FixedRateBond.FromSchedule(
            notionals: [1_000_000.0, 800_000.0, 600_000.0, 400_000.0],
            schedule: CreateTwoYearSemiannualSchedule(),
            couponRate: 0.05,
            dayCountConvention: DayCountConvention.Actual365Fixed,
            issueDate: new DateOnly(2026, 1, 1),
            paymentLagBusinessDays: 2);

        Assert.Equal(new DateOnly(2026, 7, 3), bond.Coupons.Coupons[0].PaymentDate);
        Assert.Equal(new DateOnly(2026, 7, 3), bond.RedemptionCashFlows()[0].PaymentDate);
        AssertClose(1_000_000.0, bond.CurrentNotional(new DateOnly(2026, 7, 2)), 1e-10);
        AssertClose(800_000.0, bond.CurrentNotional(new DateOnly(2026, 7, 3)), 1e-10);
    }

    [Fact]
    public void FixedRateBondPaymentLagKeepsFinalNotionalUntilFinalPaymentDate()
    {
        var bond = FixedRateBond.FromSchedule(
            faceAmount: 1_000_000.0,
            schedule: CreateTwoYearSemiannualSchedule(),
            couponRate: 0.05,
            dayCountConvention: DayCountConvention.Actual365Fixed,
            issueDate: new DateOnly(2026, 1, 1),
            paymentLagBusinessDays: 2);

        Assert.Equal(new DateOnly(2028, 1, 4), bond.Coupons.Coupons[^1].PaymentDate);
        Assert.Equal(new DateOnly(2028, 1, 4), bond.RedemptionCashFlows()[^1].PaymentDate);
        AssertClose(1_000_000.0, bond.CurrentNotional(new DateOnly(2028, 1, 1)), 1e-10);
        AssertClose(0.0, bond.CurrentNotional(new DateOnly(2028, 1, 4)), 1e-10);
        Assert.True(bond.IsTradable(new DateOnly(2028, 1, 1)));
        Assert.False(bond.IsTradable(new DateOnly(2028, 1, 4)));
    }

    [Fact]
    public void FixedRateBondPaymentLagKeepsDirtyPriceAliveUntilFinalPaymentDate()
    {
        var settlementDate = new DateOnly(2028, 1, 1);
        var bond = FixedRateBond.FromSchedule(
            faceAmount: 1_000_000.0,
            schedule: CreateTwoYearSemiannualSchedule(),
            couponRate: 0.05,
            dayCountConvention: DayCountConvention.Actual365Fixed,
            issueDate: new DateOnly(2026, 1, 1),
            paymentLagBusinessDays: 2);
        var yield = new InterestRate(0.0, CompoundingConvention.Continuous);

        var dirtyPrice = BondValuation.DirtyPrice(
            bond,
            yield,
            referenceDate: settlementDate,
            DayCountConvention.Actual365Fixed,
            settlementDate);

        Assert.True(dirtyPrice > 0.0);
        AssertClose(
            bond.CashFlows().Flows.Where(flow => flow.PaymentDate > settlementDate).Sum(flow => flow.Amount) * 100.0 / bond.FaceAmount,
            dirtyPrice,
            1e-10);
    }

    [Fact]
    public void ZeroCouponBondKeepsNotionalUntilMaturity()
    {
        var maturityDate = new DateOnly(2028, 1, 1);
        var bond = new FixedRateBond(
            faceAmount: 1_000_000.0,
            coupons: new FixedRateCashFlowLeg([]),
            maturityDate,
            issueDate: new DateOnly(2026, 1, 1));

        AssertClose(1_000_000.0, bond.CurrentNotional(new DateOnly(2027, 12, 31)), 1e-10);
        AssertClose(0.0, bond.CurrentNotional(maturityDate), 1e-10);
        Assert.True(bond.IsTradable(new DateOnly(2027, 12, 31)));
        Assert.False(bond.IsTradable(maturityDate));
    }

    [Fact]
    public void ZeroCouponBondPricesRedemptionUntilMaturity()
    {
        var settlementDate = new DateOnly(2026, 1, 1);
        var maturityDate = new DateOnly(2028, 1, 1);
        var bond = new FixedRateBond(
            faceAmount: 1_000_000.0,
            coupons: new FixedRateCashFlowLeg([]),
            maturityDate,
            issueDate: settlementDate);
        var yield = new InterestRate(0.05, CompoundingConvention.Continuous);
        var time = DayCounts.YearFraction(settlementDate, maturityDate, DayCountConvention.Actual365Fixed).Value;

        var dirtyPrice = BondValuation.DirtyPrice(
            bond,
            yield,
            referenceDate: settlementDate,
            DayCountConvention.Actual365Fixed,
            settlementDate);

        Assert.Single(bond.CashFlows().Flows);
        AssertClose(100.0 * Math.Exp(-0.05 * time), dirtyPrice, 1e-10);
        AssertClose(dirtyPrice, BondValuation.CleanPrice(
            bond,
            yield,
            referenceDate: settlementDate,
            DayCountConvention.Actual365Fixed,
            settlementDate), 1e-10);
    }

    [Fact]
    public void LevelPrincipalNotionalsDeclineLinearly()
    {
        var notionals = AmortizationNotionals.LevelPrincipal(1_000_000.0, 4);

        Assert.Equal([1_000_000.0, 750_000.0, 500_000.0, 250_000.0], notionals);
    }

    [Fact]
    public void LevelPrincipalNotionalsAvoidIntermediateOverflow()
    {
        var notionals = AmortizationNotionals.LevelPrincipal(double.MaxValue, 2);

        Assert.Equal(double.MaxValue, notionals[0]);
        AssertClose(double.MaxValue / 2.0, notionals[1], double.MaxValue * 1e-15);
    }

    [Fact]
    public void FrenchNotionalsProduceConstantDebtService()
    {
        var annualRate = 0.06;
        var paymentsPerYear = 2;
        var periodCount = 4;
        var notionals = AmortizationNotionals.French(1_000_000.0, annualRate, paymentsPerYear, periodCount);
        var periodicRate = annualRate / paymentsPerYear;
        var payments = new double[periodCount];

        for (var i = 0; i < periodCount; i++)
        {
            var current = notionals[i];
            var next = i + 1 < periodCount ? notionals[i + 1] : 0.0;
            payments[i] = current * periodicRate + (current - next);
        }

        Assert.All(payments, payment => AssertClose(payments[0], payment, 1e-8));
        Assert.True(notionals.Zip(notionals.Skip(1)).All(pair => pair.First > pair.Second));
    }

    [Fact]
    public void FrenchNotionalsDegenerateToLevelPrincipalWhenRateIsZero()
    {
        var french = AmortizationNotionals.French(1_000_000.0, annualCouponRate: 0.0, paymentsPerYear: 2, periodCount: 4);
        var level = AmortizationNotionals.LevelPrincipal(1_000_000.0, 4);

        Assert.Equal(level, french);
    }

    [Fact]
    public void AmortizationNotionalsRejectInvalidInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AmortizationNotionals.LevelPrincipal(double.NaN, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => AmortizationNotionals.LevelPrincipal(1_000_000.0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => AmortizationNotionals.French(
            1_000_000.0,
            annualCouponRate: double.PositiveInfinity,
            paymentsPerYear: 2,
            periodCount: 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => AmortizationNotionals.French(
            1_000_000.0,
            annualCouponRate: 0.05,
            paymentsPerYear: 0,
            periodCount: 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => AmortizationNotionals.French(
            double.MaxValue,
            annualCouponRate: 1.0,
            paymentsPerYear: 1,
            periodCount: 2));
    }

    [Fact]
    public void FixedRateBondIsNotTradableAtMaturity()
    {
        var bond = CreateTwoYearFixedRateBond();
        var yield = new InterestRate(0.05, CompoundingConvention.Continuous);

        Assert.False(bond.IsTradable(new DateOnly(2028, 1, 1)));
        Assert.Equal(0.0, bond.AccruedAmount(new DateOnly(2028, 1, 1)));
        Assert.Equal(0.0, BondValuation.DirtyPrice(
            bond,
            yield,
            referenceDate: new DateOnly(2028, 1, 1),
            dayCountConvention: DayCountConvention.Actual365Fixed,
            settlementDate: new DateOnly(2028, 1, 1)));
    }

    private static void AssertClose(double expected, double actual, double tolerance)
    {
        Assert.True(Math.Abs(expected - actual) <= tolerance, $"Expected {expected:R}, got {actual:R}.");
    }

    private static FixedRateBond CreateTwoYearFixedRateBond()
    {
        var schedule = CreateTwoYearSemiannualSchedule();

        return FixedRateBond.FromSchedule(
            faceAmount: 1_000_000.0,
            schedule: schedule,
            couponRate: 0.05,
            dayCountConvention: DayCountConvention.Actual365Fixed,
            issueDate: new DateOnly(2026, 1, 1));
    }

    private static FixedRateBond CreateTwoYearExCouponFixedRateBond()
    {
        var schedule = CreateTwoYearSemiannualSchedule();

        return FixedRateBond.FromSchedule(
            faceAmount: 1_000_000.0,
            schedule: schedule,
            couponRate: 0.05,
            dayCountConvention: DayCountConvention.Actual365Fixed,
            issueDate: new DateOnly(2026, 1, 1),
            exCouponBusinessDays: 2);
    }

    private static FixedRateBond CreateAmortizingFixedRateBond()
    {
        return FixedRateBond.FromSchedule(
            notionals: [1_000_000.0, 800_000.0, 600_000.0, 400_000.0],
            schedule: CreateTwoYearSemiannualSchedule(),
            couponRate: 0.05,
            dayCountConvention: DayCountConvention.Actual365Fixed,
            issueDate: new DateOnly(2026, 1, 1));
    }

    private static FixedRateBond CreateTwoYearCompoundedCouponBond(double couponRate)
    {
        return FixedRateBond.FromSchedule(
            faceAmount: 1_000_000.0,
            schedule: CreateTwoYearSemiannualSchedule(),
            couponRate: couponRate,
            dayCountConvention: DayCountConvention.Actual365Fixed,
            issueDate: new DateOnly(2026, 1, 1),
            compounding: CompoundingConvention.Compounded,
            frequency: 2);
    }

    private static FixedRateBond CreateExtremeContinuousCouponBond()
    {
        var coupon = new FixedRateCashFlow(
            PaymentDate: new DateOnly(2027, 1, 1),
            AccrualStartDate: new DateOnly(2026, 1, 1),
            AccrualEndDate: new DateOnly(2027, 1, 1),
            Nominal: 1e10,
            Rate: 700.0,
            AccrualYearFraction: 1.0,
            DayCountConvention: DayCountConvention.Actual365Fixed,
            Compounding: CompoundingConvention.Continuous);

        return new FixedRateBond(
            faceAmount: 1e10,
            coupons: new FixedRateCashFlowLeg([coupon]),
            maturityDate: new DateOnly(2027, 1, 1),
            issueDate: new DateOnly(2026, 1, 1));
    }

    private static FixedRateBond CreateTinyNotionalHugeCouponBond()
    {
        var coupon = new FixedRateCashFlow(
            PaymentDate: new DateOnly(2027, 1, 1),
            AccrualStartDate: new DateOnly(2026, 1, 1),
            AccrualEndDate: new DateOnly(2027, 1, 1),
            Nominal: 1e-300,
            Rate: 1e308,
            AccrualYearFraction: 1.0,
            DayCountConvention: DayCountConvention.Actual365Fixed,
            Compounding: CompoundingConvention.Simple);

        return new FixedRateBond(
            faceAmount: 1e-300,
            coupons: new FixedRateCashFlowLeg([coupon]),
            maturityDate: new DateOnly(2027, 1, 1),
            issueDate: new DateOnly(2026, 1, 1));
    }

    private static DiscountCurve CreateBondDiscountCurve()
    {
        return new DiscountCurve(
        [
            new CurvePoint(0.0, 1.0000),
            new CurvePoint(0.5, 0.9750),
            new CurvePoint(1.0, 0.9520),
            new CurvePoint(1.5, 0.9300),
            new CurvePoint(2.0, 0.9080)
        ]);
    }

    private static double CentralNpvDerivative(
        CashFlowLeg leg,
        DateOnly referenceDate,
        CompoundingConvention compounding,
        int frequency,
        double rate)
    {
        const double bump = 1e-5;
        var up = NpvAtRate(leg, referenceDate, compounding, frequency, rate + bump);
        var down = NpvAtRate(leg, referenceDate, compounding, frequency, rate - bump);
        return (up - down) / (2.0 * bump);
    }

    private static double CentralNpvSecondDerivative(
        CashFlowLeg leg,
        DateOnly referenceDate,
        CompoundingConvention compounding,
        int frequency,
        double rate)
    {
        const double bump = 1e-4;
        var up = NpvAtRate(leg, referenceDate, compounding, frequency, rate + bump);
        var center = NpvAtRate(leg, referenceDate, compounding, frequency, rate);
        var down = NpvAtRate(leg, referenceDate, compounding, frequency, rate - bump);
        return (up - 2.0 * center + down) / (bump * bump);
    }

    private static double NpvAtRate(
        CashFlowLeg leg,
        DateOnly referenceDate,
        CompoundingConvention compounding,
        int frequency,
        double rate)
    {
        return CashFlowValuation.NetPresentValue(
            leg,
            new InterestRate(rate, compounding, frequency),
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate: referenceDate,
            npvDate: referenceDate);
    }

    private static double CentralDirtyPriceCouponDerivative(
        Func<double, FixedRateBond> bondFactory,
        DiscountCurve curve,
        DateOnly referenceDate,
        DateOnly settlementDate,
        double rate)
    {
        const double bump = 1e-5;
        var up = BondValuation.DirtyPrice(
            bondFactory(rate + bump),
            curve,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate);
        var down = BondValuation.DirtyPrice(
            bondFactory(rate - bump),
            curve,
            referenceDate,
            DayCountConvention.Actual365Fixed,
            settlementDate);

        return (up - down) / (2.0 * bump);
    }

    private static DateSchedule CreateTwoYearSemiannualSchedule()
    {
        return ScheduleGenerator.Generate(ScheduleInput.Forward(
            new DateOnly(2026, 1, 1),
            new DateOnly(2028, 1, 1),
            ScheduleTenor.Semiannual,
            calendar: HolidayCalendar.WeekendsOnly,
            convention: BusinessDayConvention.Unadjusted));
    }
}
