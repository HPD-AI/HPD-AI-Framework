using Rhodium.HFT;
using Rhodium.Primitives;

namespace Rhodium.Simulation;

/// <summary>
/// Slippage calculation logic.
/// </summary>
public static class SlippageCalculator
{
    /// <summary>
    /// Apply slippage to nominal fill price based on order size.
    /// Slippage is always adverse: buyers pay more, sellers receive less.
    /// </summary>
    public static Price ApplySlippage(
        Price nominalFillPrice,
        Qty fillQty,
        Side side,
        SlippageParams slippage,
        IHftDepth depth)
    {
        return slippage.Apply(nominalFillPrice, fillQty, side);
    }
}
