namespace Rhodium.Primitives;

/// <summary>
/// Replay-visible account transfer category.
/// </summary>
public enum AccountTransferType : byte
{
    CashDeposit = 1,
    CashWithdrawal = 2,
    AssetDeposit = 3,
    AssetWithdrawal = 4,
    InternalTransfer = 5
}
