namespace Helium.Finance.CashFlows;

public sealed class FixedRateCashFlowLeg
{
    public FixedRateCashFlowLeg(IEnumerable<FixedRateCashFlow> coupons)
    {
        ArgumentNullException.ThrowIfNull(coupons);

        Coupons = coupons
            .OrderBy(coupon => coupon.PaymentDate)
            .ToArray();

        foreach (var coupon in Coupons)
            coupon.Validate();
    }

    public IReadOnlyList<FixedRateCashFlow> Coupons { get; }

    public int Count => Coupons.Count;

    public bool IsEmpty => Coupons.Count == 0;

    public DateOnly StartDate
    {
        get
        {
            EnsureNotEmpty();
            return Coupons[0].AccrualStartDate;
        }
    }

    public DateOnly MaturityDate
    {
        get
        {
            EnsureNotEmpty();
            return Coupons[^1].PaymentDate;
        }
    }

    public CashFlowLeg ToSimpleLeg() => new(Coupons.Select(coupon => coupon.ToSimpleCashFlow()));

    public FixedRateCashFlow? PreviousCoupon(DateOnly settlementDate, bool includeSettlementDateFlows = false)
    {
        for (var i = Coupons.Count - 1; i >= 0; i--)
        {
            if (CashFlowLeg.HasOccurred(Coupons[i].PaymentDate, settlementDate, includeSettlementDateFlows))
                return Coupons[i];
        }

        return null;
    }

    public FixedRateCashFlow? NextCoupon(DateOnly settlementDate, bool includeSettlementDateFlows = false)
    {
        for (var i = 0; i < Coupons.Count; i++)
        {
            if (!CashFlowLeg.HasOccurred(Coupons[i].PaymentDate, settlementDate, includeSettlementDateFlows))
                return Coupons[i];
        }

        return null;
    }

    public double AccruedAmount(DateOnly settlementDate, bool includeSettlementDateFlows = false)
    {
        var nextCoupon = NextCoupon(settlementDate, includeSettlementDateFlows);
        if (nextCoupon is null)
            return 0.0;

        var paymentDate = nextCoupon.Value.PaymentDate;
        var accrued = 0.0;
        foreach (var coupon in Coupons)
        {
            if (coupon.PaymentDate == paymentDate)
                accrued += coupon.AccruedAmount(settlementDate);
        }

        return accrued;
    }

    public int AccruedDays(DateOnly settlementDate, bool includeSettlementDateFlows = false)
    {
        var nextCoupon = NextCoupon(settlementDate, includeSettlementDateFlows);
        return nextCoupon?.AccruedDays(settlementDate) ?? 0;
    }

    private void EnsureNotEmpty()
    {
        if (Coupons.Count == 0)
            throw new InvalidOperationException("Fixed-rate cash-flow leg is empty.");
    }
}
