using Rhodium.Primitives;

namespace Rhodium.Connectivity.Simulation;

/// <summary>
/// Fee calculation logic for simulation.
/// </summary>
public static class FeeCalculator
{
    /// <summary>
    /// Calculate commission for a fill based on fee parameters.
    /// Assumes market orders are takers, limit orders are makers.
    /// </summary>
    public static Money Calculate(this FeeParams fees, Qty quantity, Price price, bool isMaker = true)
    {
        return fees.Model switch
        {
            FeeModelType.PercentageOfValue => CalculatePercentage(fees, quantity, price, isMaker),
            FeeModelType.PerQuantity => CalculatePerQuantity(fees, quantity, isMaker),
            FeeModelType.PerTrade => fees.FixedFee,
            FeeModelType.TieredByVolume => CalculateTiered(fees, quantity, price),
            FeeModelType.Directional => CalculateDirectional(fees, quantity, price),
            _ => Money.Zero(price.Currency)
        };
    }

    private static Money CalculatePercentage(FeeParams fees, Qty quantity, Price price, bool isMaker)
    {
        var bps = isMaker ? fees.MakerBps : fees.TakerBps;
        var notional = quantity.Value * price.Value;
        var feeAmount = notional * bps / 10000m;
        return new Money(feeAmount, price.Currency);
    }

    private static Money CalculatePerQuantity(FeeParams fees, Qty quantity, bool isMaker)
    {
        var feePerLot = isMaker ? fees.MakerFeePerLot : fees.TakerFeePerLot;
        return new Money(quantity.Value * feePerLot.Amount, feePerLot.Currency);
    }

    private static Money CalculateTiered(FeeParams fees, Qty quantity, Price price)
    {
        // Simplified: use base tier
        // Real implementation would track 30-day volume
        var notional = quantity.Value * price.Value;
        var bps = fees.TakerBps; // Default to taker for simplicity
        return new Money(notional * bps / 10000m, price.Currency);
    }

    private static Money CalculateDirectional(FeeParams fees, Qty quantity, Price price)
    {
        // Simplified: treat as percentage model
        var notional = quantity.Value * price.Value;
        var bps = fees.TakerBps;
        return new Money(notional * bps / 10000m, price.Currency);
    }
}
