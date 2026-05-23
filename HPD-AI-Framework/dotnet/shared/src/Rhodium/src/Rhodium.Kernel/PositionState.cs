using System.Runtime.InteropServices;
using Rhodium.Primitives;

namespace Rhodium.Kernel;

/// <summary>
/// Unmanaged hot-path position state. Rich Position objects are boundary objects,
/// not WorldState storage.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PositionState
{
    public decimal Quantity;
    public decimal AvgEntryPrice;
    public decimal RealizedPnL;

    public readonly bool IsFlat => Quantity == 0m;

    public void ApplyFill(Side side, Qty qty, Price price, Money commission)
    {
        var fillSign = side == Side.Buy ? 1m : -1m;
        var fillQty = qty.Value * fillSign;
        var newQty = Quantity + fillQty;
        var isAdding = (Quantity >= 0 && fillSign > 0) || (Quantity <= 0 && fillSign < 0);

        if (isAdding || Quantity == 0m)
        {
            var totalCost = Math.Abs(Quantity) * AvgEntryPrice + qty.Value * price.Value;
            AvgEntryPrice = Math.Abs(newQty) > 0m ? totalCost / Math.Abs(newQty) : 0m;
            Quantity = newQty;
            RealizedPnL -= commission.Amount;
            return;
        }

        var closingQty = Math.Min(qty.Value, Math.Abs(Quantity));
        var pnl = (price.Value - AvgEntryPrice) * closingQty * (Quantity > 0m ? 1m : -1m);
        Quantity = newQty;
        RealizedPnL += pnl - commission.Amount;

        if (Math.Abs(Quantity) < 0.0000001m)
        {
            Quantity = 0m;
            AvgEntryPrice = 0m;
        }
    }
}
