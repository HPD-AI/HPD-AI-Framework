using Rhodium.Primitives;

namespace Rhodium.Connectivity.Simulation;

/// <summary>
/// Slippage calculation helper for simulation.
/// </summary>
public static class SlippageHelper
{
    /// <summary>
    /// Calculate slippage amount based on order parameters.
    /// Returns the amount to add to the price (can be negative for sells).
    /// </summary>
    public static Money Calculate(this SlippageParams slippage, Price price, Qty quantity, Side side)
    {
        if (slippage.Model == SlippageModelType.None)
            return Money.Zero(price.Currency);

        // VolumeProportional: slippage increases with order size
        decimal slippageAmount = quantity.Value * slippage.BpsPerLotSize * price.Value / 10000m;

        // Apply adverse slippage: buyers pay more, sellers receive less
        var signedSlippage = side == Side.Buy ? slippageAmount : -slippageAmount;
        return new Money(signedSlippage, price.Currency);
    }
}
