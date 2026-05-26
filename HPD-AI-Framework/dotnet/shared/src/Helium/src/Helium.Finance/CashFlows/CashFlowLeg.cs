namespace Helium.Finance.CashFlows;

public sealed class CashFlowLeg
{
    public CashFlowLeg(IEnumerable<SimpleCashFlow> cashFlows)
    {
        ArgumentNullException.ThrowIfNull(cashFlows);

        Flows = cashFlows
            .OrderBy(cashFlow => cashFlow.PaymentDate)
            .ToArray();

        foreach (var cashFlow in Flows)
            cashFlow.Validate();
    }

    public IReadOnlyList<SimpleCashFlow> Flows { get; }

    public int Count => Flows.Count;

    public bool IsEmpty => Flows.Count == 0;

    public DateOnly StartDate
    {
        get
        {
            EnsureNotEmpty();
            return Flows[0].PaymentDate;
        }
    }

    public DateOnly MaturityDate
    {
        get
        {
            EnsureNotEmpty();
            return Flows[^1].PaymentDate;
        }
    }

    public SimpleCashFlow? PreviousCashFlow(DateOnly settlementDate, bool includeSettlementDateFlows = false)
    {
        for (var i = Flows.Count - 1; i >= 0; i--)
        {
            if (HasOccurred(Flows[i].PaymentDate, settlementDate, includeSettlementDateFlows))
                return Flows[i];
        }

        return null;
    }

    public SimpleCashFlow? NextCashFlow(DateOnly settlementDate, bool includeSettlementDateFlows = false)
    {
        for (var i = 0; i < Flows.Count; i++)
        {
            if (!HasOccurred(Flows[i].PaymentDate, settlementDate, includeSettlementDateFlows))
                return Flows[i];
        }

        return null;
    }

    public double AmountOn(DateOnly paymentDate)
    {
        var sum = 0.0;
        foreach (var cashFlow in Flows)
        {
            if (cashFlow.PaymentDate == paymentDate)
                sum = AddFinite(sum, cashFlow.Amount);
        }

        return sum;
    }

    public bool IsExpired(DateOnly settlementDate, bool includeSettlementDateFlows = false) =>
        NextCashFlow(settlementDate, includeSettlementDateFlows) is null;

    public static bool HasOccurred(
        DateOnly paymentDate,
        DateOnly settlementDate,
        bool includeSettlementDateFlows = false)
    {
        return includeSettlementDateFlows
            ? paymentDate < settlementDate
            : paymentDate <= settlementDate;
    }

    private void EnsureNotEmpty()
    {
        if (Flows.Count == 0)
            throw new InvalidOperationException("Cash-flow leg is empty.");
    }

    private static double AddFinite(double left, double right)
    {
        var result = left + right;
        if (!double.IsFinite(result))
            throw new ArgumentOutOfRangeException(nameof(right), "Cash-flow amount aggregate must be finite.");

        return result;
    }
}
