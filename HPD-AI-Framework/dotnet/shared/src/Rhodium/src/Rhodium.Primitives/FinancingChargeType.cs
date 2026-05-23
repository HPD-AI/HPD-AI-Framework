namespace Rhodium.Primitives;

/// <summary>
/// Replay-visible financing cash-flow category.
/// </summary>
public enum FinancingChargeType : byte
{
    CashInterestCredit = 1,
    CashInterestDebit = 2,
    BorrowFee = 3,
    PerpetualFunding = 4,
    ForexRollover = 5
}
