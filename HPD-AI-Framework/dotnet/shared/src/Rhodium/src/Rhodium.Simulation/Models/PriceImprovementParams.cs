using Rhodium.Primitives;

namespace Rhodium.Simulation;

/// <summary>
/// Replay/simulation price-improvement policy.
/// Price improvement is always favorable: buys pay less, sells receive more.
/// </summary>
public readonly record struct PriceImprovementParams(
    PriceImprovementModelType Model,
    decimal TakerBps = 0m,
    decimal MakerBps = 0m)
{
    public static readonly PriceImprovementParams None = new(PriceImprovementModelType.None);

    public static PriceImprovementParams FixedBps(decimal takerBps, decimal makerBps = 0m)
    {
        if (takerBps < 0m)
            throw new ArgumentOutOfRangeException(nameof(takerBps), "Taker price improvement cannot be negative.");
        if (makerBps < 0m)
            throw new ArgumentOutOfRangeException(nameof(makerBps), "Maker price improvement cannot be negative.");

        return new(PriceImprovementModelType.FixedBps, takerBps, makerBps);
    }

    public Price Apply(Price price, Side side, bool isMaker)
    {
        if (Model == PriceImprovementModelType.None || price.Value <= 0m)
            return price;

        var bps = isMaker ? MakerBps : TakerBps;
        if (bps <= 0m)
            return price;

        var adjustment = price.Value * bps / 10_000m;
        var improved = side == Side.Buy
            ? Math.Max(0m, price.Value - adjustment)
            : price.Value + adjustment;
        return new Price(improved, price.Currency);
    }
}

public enum PriceImprovementModelType : byte
{
    None = 0,
    FixedBps = 1
}
