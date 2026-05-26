using Helium.Finance.Conventions;
using Helium.Finance.Schedules;

namespace Helium.Finance.CashFlows;

public sealed class FixedRateBond
{
    public FixedRateBond(
        double faceAmount,
        FixedRateCashFlowLeg coupons,
        DateOnly maturityDate,
        DateOnly? issueDate = null,
        double redemptionAmount = 100.0)
    {
        ArgumentNullException.ThrowIfNull(coupons);

        if (!double.IsFinite(faceAmount) || faceAmount <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(faceAmount), "Face amount must be finite and positive.");

        if (!double.IsFinite(redemptionAmount) || redemptionAmount < 0.0)
            throw new ArgumentOutOfRangeException(nameof(redemptionAmount), "Redemption amount must be finite and nonnegative.");

        if (issueDate is { } issue && issue >= maturityDate)
            throw new ArgumentOutOfRangeException(nameof(issueDate), "Issue date must be before maturity date.");

        if (issueDate is { } datedIssue && !coupons.IsEmpty && datedIssue >= coupons.Coupons[0].PaymentDate)
            throw new ArgumentOutOfRangeException(nameof(issueDate), "Issue date must be before the first coupon payment date.");

        FaceAmount = faceAmount;
        Coupons = coupons;
        MaturityDate = maturityDate;
        IssueDate = issueDate;
        RedemptionAmount = redemptionAmount;
    }

    public double FaceAmount { get; }

    public FixedRateCashFlowLeg Coupons { get; }

    public DateOnly MaturityDate { get; }

    public DateOnly? IssueDate { get; }

    public double RedemptionAmount { get; }

    public DateOnly StartDate => Coupons.IsEmpty ? IssueDate ?? MaturityDate : Coupons.StartDate;

    public double CurrentNotional(DateOnly settlementDate)
    {
        if (Coupons.IsEmpty)
            return settlementDate < MaturityDate ? FaceAmount : 0.0;

        foreach (var coupon in Coupons.Coupons)
        {
            if (settlementDate < coupon.PaymentDate)
                return coupon.Nominal;
        }

        return 0.0;
    }

    public bool IsTradable(DateOnly settlementDate) => CurrentNotional(settlementDate) > 0.0;

    public double AccruedAmount(DateOnly settlementDate)
    {
        var currentNotional = CurrentNotional(settlementDate);
        if (currentNotional == 0.0)
            return 0.0;

        return Coupons.AccruedAmount(settlementDate) * 100.0 / currentNotional;
    }

    public CashFlowLeg CashFlows()
    {
        var flows = Coupons.Coupons.Select(coupon => coupon.ToSimpleCashFlow())
            .Concat(RedemptionCashFlows());

        return new CashFlowLeg(flows);
    }

    public IReadOnlyList<SimpleCashFlow> RedemptionCashFlows()
    {
        if (Coupons.IsEmpty)
            return [new SimpleCashFlow(MaturityDate, FaceAmount * RedemptionAmount / 100.0)];

        var redemptions = new List<SimpleCashFlow>();
        for (var i = 0; i < Coupons.Coupons.Count; i++)
        {
            var currentNotional = Coupons.Coupons[i].Nominal;
            var nextNotional = i + 1 < Coupons.Coupons.Count ? Coupons.Coupons[i + 1].Nominal : 0.0;
            var redeemedNotional = currentNotional - nextNotional;
            if (redeemedNotional < -1e-10)
                throw new InvalidOperationException("Fixed-rate bond notionals must be nonincreasing.");

            if (redeemedNotional > 0.0)
                redemptions.Add(new SimpleCashFlow(Coupons.Coupons[i].PaymentDate, redeemedNotional * RedemptionAmount / 100.0));
        }

        return redemptions;
    }

    public static FixedRateBond FromSchedule(
        double faceAmount,
        DateSchedule schedule,
        double couponRate,
        DayCountConvention dayCountConvention,
        DateOnly? issueDate = null,
        double redemptionAmount = 100.0,
        int exCouponBusinessDays = 0,
        int paymentLagBusinessDays = 0,
        CompoundingConvention compounding = CompoundingConvention.Simple,
        int frequency = 1)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        var coupons = FixedRateCashFlow.FromSchedule(
            schedule,
            faceAmount,
            couponRate,
            dayCountConvention,
            exCouponBusinessDays,
            paymentLagBusinessDays: paymentLagBusinessDays,
            compounding: compounding,
            frequency: frequency);

        return new FixedRateBond(
            faceAmount,
            new FixedRateCashFlowLeg(coupons),
            schedule.EndDate,
            issueDate,
            redemptionAmount);
    }

    public static FixedRateBond FromSchedule(
        IReadOnlyList<double> notionals,
        DateSchedule schedule,
        double couponRate,
        DayCountConvention dayCountConvention,
        DateOnly? issueDate = null,
        double redemptionAmount = 100.0,
        int exCouponBusinessDays = 0,
        int paymentLagBusinessDays = 0,
        CompoundingConvention compounding = CompoundingConvention.Simple,
        int frequency = 1)
    {
        ArgumentNullException.ThrowIfNull(notionals);
        ArgumentNullException.ThrowIfNull(schedule);

        if (notionals.Count != schedule.Dates.Count - 1)
            throw new ArgumentException("Notional count must match the number of schedule periods.", nameof(notionals));

        for (var i = 0; i < notionals.Count; i++)
        {
            if (!double.IsFinite(notionals[i]) || notionals[i] <= 0.0)
                throw new ArgumentOutOfRangeException(nameof(notionals), "Notionals must be finite and positive.");

            if (i > 0 && notionals[i] > notionals[i - 1])
                throw new ArgumentOutOfRangeException(nameof(notionals), "Notionals must be nonincreasing.");
        }

        var coupons = new FixedRateCashFlow[notionals.Count];
        if (exCouponBusinessDays < 0)
            throw new ArgumentOutOfRangeException(nameof(exCouponBusinessDays), "Ex-coupon business days must be nonnegative.");

        if (paymentLagBusinessDays < 0)
            throw new ArgumentOutOfRangeException(nameof(paymentLagBusinessDays), "Payment lag business days must be nonnegative.");

        _ = new InterestRate(couponRate, compounding, frequency).CompoundFactor(0.0);

        for (var i = 0; i < coupons.Length; i++)
        {
            var accrualStart = schedule.Dates[i];
            var accrualEnd = schedule.Dates[i + 1];
            var accrual = DayCounts.YearFraction(accrualStart, accrualEnd, dayCountConvention).Value;
            var paymentDate = paymentLagBusinessDays == 0
                ? accrualEnd
                : schedule.Input.Calendar.Adjust(schedule.Input.Calendar.AdvanceBusinessDays(accrualEnd, paymentLagBusinessDays), Calendars.BusinessDayConvention.Following);
            var exCouponDate = exCouponBusinessDays == 0
                ? (DateOnly?)null
                : schedule.Input.Calendar.Adjust(schedule.Input.Calendar.AdvanceBusinessDays(paymentDate, -exCouponBusinessDays), Calendars.BusinessDayConvention.Preceding);

            coupons[i] = new FixedRateCashFlow(
                PaymentDate: paymentDate,
                AccrualStartDate: accrualStart,
                AccrualEndDate: accrualEnd,
                Nominal: notionals[i],
                Rate: couponRate,
                AccrualYearFraction: accrual,
                DayCountConvention: dayCountConvention,
                Compounding: compounding,
                Frequency: frequency,
                ExCouponDate: exCouponDate);
        }

        return new FixedRateBond(
            notionals[0],
            new FixedRateCashFlowLeg(coupons),
            schedule.EndDate,
            issueDate,
            redemptionAmount);
    }
}
