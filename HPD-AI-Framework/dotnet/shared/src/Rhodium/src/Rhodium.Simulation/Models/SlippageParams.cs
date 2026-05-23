using Rhodium.Primitives;

namespace Rhodium.Simulation;

/// <summary>
/// Slippage model type.
/// </summary>
public enum SlippageModelType : byte
{
    None = 0,
    VolumeProportional = 1,
    VolatilityAdjusted = 2
}

/// <summary>
/// Slippage simulation parameters.
/// </summary>
public sealed record SlippageParams(
    SlippageModelType Model,
    decimal BpsPerLotSize = 0m,
    decimal ReferenceQuantity = 0m,
    decimal VolatilityBps = 0m)
{
    public static readonly SlippageParams None = new(SlippageModelType.None);

    public static SlippageParams VolumeProportional(
        decimal bpsPerLotSize,
        decimal referenceQuantity = 0m)
        => new(
            SlippageModelType.VolumeProportional,
            bpsPerLotSize,
            referenceQuantity);

    public static SlippageParams VolatilityAdjusted(
        decimal bpsPerLotSize,
        decimal volatilityBps,
        decimal referenceQuantity = 0m)
        => new(
            SlippageModelType.VolatilityAdjusted,
            bpsPerLotSize,
            referenceQuantity,
            volatilityBps);

    public Price Apply(Price price, Qty quantity, Side side)
    {
        if (Model == SlippageModelType.None || quantity.Value == 0m)
            return price;

        var slippageBps = CalculateBps(quantity);
        if (slippageBps == 0m)
            return price;

        var adjustment = price.Value * slippageBps / 10_000m;
        var adjusted = side == Side.Buy
            ? price.Value + adjustment
            : Math.Max(0m, price.Value - adjustment);
        return new Price(adjusted, price.Currency);
    }

    internal decimal CalculateBps(Qty quantity)
    {
        var quantityFactor = ReferenceQuantity > 0m
            ? quantity.Value / ReferenceQuantity
            : quantity.Value;
        var volumeBps = quantityFactor * BpsPerLotSize;

        return Model switch
        {
            SlippageModelType.None => 0m,
            SlippageModelType.VolumeProportional => volumeBps,
            SlippageModelType.VolatilityAdjusted => volumeBps + VolatilityBps,
            _ => 0m
        };
    }
}

/// <summary>
/// Fill behavior for partial fills.
/// </summary>
public enum FillBehavior : byte
{
    NoPartialFill = 0,
    FillOnTouch = 1,
    PartialFillOnTrade = 2
}

/// <summary>
/// Bar processing order for OHLC prices.
/// </summary>
public enum BarOrderingMode : byte
{
    /// <summary>
    /// Fixed ordering: Always Open → High → Low → Close.
    /// Simple and deterministic (50% accuracy for H/L sequence).
    /// </summary>
    Fixed = 0,

    /// <summary>
    /// Adaptive ordering based on bar structure (research-backed).
    /// - If Open closer to High: Open → High → Low → Close
    /// - If Open closer to Low: Open → Low → High → Close
    /// Achieves 75-85% accuracy vs 50% with fixed ordering.
    /// Reference: https://gist.github.com/stefansimik/d387e1d9ff784a8973feca0cde51e363
    /// </summary>
    Adaptive = 1
}
