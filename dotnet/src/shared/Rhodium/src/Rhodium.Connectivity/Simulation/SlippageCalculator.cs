using Rhodium.HFT;
using Rhodium.Primitives;

namespace Rhodium.Connectivity.Simulation;

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
        if (slippage.Model == SlippageModelType.None)
            return nominalFillPrice;

        // VolumeProportional: slippage increases with order size
        if (slippage.Model == SlippageModelType.VolumeProportional)
        {
            // Calculate slippage in basis points
            var slippageBps = fillQty.Value * slippage.BpsPerLotSize;
            var slippageAmount = nominalFillPrice.Value * slippageBps / 10000m;

            // Apply adverse slippage
            var adjustedPrice = side == Side.Buy
                ? nominalFillPrice.Value + slippageAmount  // Buyers pay more
                : nominalFillPrice.Value - slippageAmount; // Sellers receive less

            return new Price(Math.Max(0, adjustedPrice), nominalFillPrice.Currency);
        }

        return nominalFillPrice;
    }
}
