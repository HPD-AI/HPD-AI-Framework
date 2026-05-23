using Rhodium.Primitives;

namespace Rhodium.Simulation;

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
        => Calculate(fees, quantity, price, Side.None, isMaker);

    /// <summary>
    /// Calculate commission for a fill based on fee parameters and order direction.
    /// Directional fee models require the order side; other models ignore it.
    /// </summary>
    public static Money Calculate(this FeeParams fees, Qty quantity, Price price, Side side, bool isMaker = true)
        => Calculate(fees, quantity, price, side, isMaker, Money.Zero(price.Currency));

    /// <summary>
    /// Calculate commission for a fill with account rolling volume for tiered fee schedules.
    /// </summary>
    public static Money Calculate(
        this FeeParams fees,
        Qty quantity,
        Price price,
        Side side,
        bool isMaker,
        Money thirtyDayVolume)
    {
        return fees.Model switch
        {
            FeeModelType.PercentageOfValue => CalculatePercentage(fees, quantity, price, isMaker),
            FeeModelType.PerQuantity => CalculatePerQuantity(fees, quantity, isMaker),
            FeeModelType.PerTrade => fees.FixedFee,
            FeeModelType.TieredByVolume => CalculateTiered(fees, quantity, price, isMaker, thirtyDayVolume),
            FeeModelType.Directional => CalculateDirectional(fees, quantity, price, side),
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

    private static Money CalculateTiered(
        FeeParams fees,
        Qty quantity,
        Price price,
        bool isMaker,
        Money thirtyDayVolume)
    {
        var notional = quantity.Value * price.Value;
        var (makerBps, takerBps) = fees.TieredSchedule?.GetFeeRate(thirtyDayVolume)
            ?? (fees.MakerBps, fees.TakerBps);
        var bps = isMaker ? makerBps : takerBps;
        return new Money(notional * bps / 10000m, price.Currency);
    }

    private static Money CalculateDirectional(FeeParams fees, Qty quantity, Price price, Side side)
    {
        var notional = quantity.Value * price.Value;
        var bps = side switch
        {
            Side.Buy => fees.BuyFeeBps,
            Side.Sell => fees.SellFeeBps,
            _ => Math.Max(fees.BuyFeeBps, fees.SellFeeBps)
        };
        return new Money(notional * bps / 10000m, price.Currency);
    }
}
