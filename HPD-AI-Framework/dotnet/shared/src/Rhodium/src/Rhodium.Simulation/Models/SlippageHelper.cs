using Rhodium.Primitives;

namespace Rhodium.Simulation;

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

        var adjusted = slippage.Apply(price, quantity, side);
        return new Money(adjusted.Value - price.Value, price.Currency);
    }

    public static Money Calculate(
        this SlippageParams slippage,
        Price price,
        Qty quantity,
        Side side,
        decimal referenceQuantity,
        decimal volatilityBps = 0m)
        => (slippage with
        {
            ReferenceQuantity = referenceQuantity,
            VolatilityBps = slippage.Model == SlippageModelType.VolatilityAdjusted
                ? volatilityBps
                : slippage.VolatilityBps
        }).Calculate(price, quantity, side);
}
