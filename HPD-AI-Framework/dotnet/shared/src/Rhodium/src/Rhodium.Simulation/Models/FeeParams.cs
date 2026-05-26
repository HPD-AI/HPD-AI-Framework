using Rhodium.Primitives;

namespace Rhodium.Simulation;

/// <summary>
/// Fee calculation method.
/// </summary>
public enum FeeModelType : byte
{
    /// <summary>
    /// Fee as percentage of trade value (most common).
    /// Fee = (quantity × price) × (bps / 10000)
    /// </summary>
    PercentageOfValue = 0,

    /// <summary>
    /// Fee per unit quantity traded.
    /// Fee = quantity × feePerLot
    /// Common in commodity futures.
    /// </summary>
    PerQuantity = 1,

    /// <summary>
    /// Fixed fee per trade (regardless of size).
    /// Fee = fixedAmount
    /// Common in retail equity markets.
    /// </summary>
    PerTrade = 2,

    /// <summary>
    /// Tiered fee based on 30-day volume.
    /// Fee rate decreases with higher volume tier.
    /// </summary>
    TieredByVolume = 3,

    /// <summary>
    /// Different fees by direction (buy vs sell).
    /// Used in some crypto exchanges.
    /// </summary>
    Directional = 4,

    /// <summary>
    /// Use the registered instrument contract's FeeTerms.
    /// </summary>
    ContractTerms = 5
}

/// <summary>
/// Fee model parameters.
/// </summary>
public sealed record FeeParams
{
    public required FeeModelType Model { get; init; }

    // PercentageOfValue parameters
    public decimal MakerBps { get; init; } = 0m;
    public decimal TakerBps { get; init; } = 0m;

    // PerQuantity parameters
    public Money MakerFeePerLot { get; init; } = Money.Zero(Currency.USD);
    public Money TakerFeePerLot { get; init; } = Money.Zero(Currency.USD);

    // PerTrade parameters
    public Money FixedFee { get; init; } = Money.Zero(Currency.USD);

    // TieredByVolume parameters
    public TieredFeeSchedule? TieredSchedule { get; init; }

    // Directional parameters
    public decimal BuyFeeBps { get; init; } = 0m;
    public decimal SellFeeBps { get; init; } = 0m;

    // ==================== PRESET FACTORY METHODS ====================

    public static readonly FeeParams Zero = new()
    {
        Model = FeeModelType.PercentageOfValue
    };

    public static readonly FeeParams ContractTerms = new()
    {
        Model = FeeModelType.ContractTerms
    };

    public static FeeParams MakerTaker(decimal makerBps, decimal takerBps) => new()
    {
        Model = FeeModelType.PercentageOfValue,
        MakerBps = makerBps,
        TakerBps = takerBps
    };

    public static FeeParams PerLot(Money makerFee, Money takerFee) => new()
    {
        Model = FeeModelType.PerQuantity,
        MakerFeePerLot = makerFee,
        TakerFeePerLot = takerFee
    };

    public static FeeParams Fixed(Money feePerTrade) => new()
    {
        Model = FeeModelType.PerTrade,
        FixedFee = feePerTrade
    };

    public static FeeParams Directional(decimal buyBps, decimal sellBps) => new()
    {
        Model = FeeModelType.Directional,
        BuyFeeBps = buyBps,
        SellFeeBps = sellBps
    };

    public static FeeParams Tiered(TieredFeeSchedule schedule) => new()
    {
        Model = FeeModelType.TieredByVolume,
        TieredSchedule = schedule
    };

    // Exchange-specific presets
    public static FeeParams BinanceFutures() => MakerTaker(makerBps: 2m, takerBps: 4m);
    public static FeeParams CoinbaseAdvanced() => MakerTaker(makerBps: 40m, takerBps: 60m);

    public static FeeParams InteractiveBrokers() => new()
    {
        Model = FeeModelType.PerQuantity,
        MakerFeePerLot = new Money(0.005m, Currency.USD),
        TakerFeePerLot = new Money(0.005m, Currency.USD)
    };
}

/// <summary>
/// Tiered fee schedule (for TieredByVolume model).
/// </summary>
public sealed record TieredFeeSchedule
{
    public required IReadOnlyList<FeeTier> Tiers { get; init; }

    public (decimal MakerBps, decimal TakerBps) GetFeeRate(Money thirtyDayVolume)
    {
        if (Tiers.Count == 0)
            return (0m, 0m);

        var fallback = Tiers[0];
        FeeTier? selected = null;
        for (var i = 0; i < Tiers.Count; i++)
        {
            var tier = Tiers[i];
            if (tier.MinVolume.Amount < fallback.MinVolume.Amount)
                fallback = tier;

            if (thirtyDayVolume.Amount < tier.MinVolume.Amount)
                continue;

            if (selected is null || tier.MinVolume.Amount > selected.MinVolume.Amount)
                selected = tier;
        }

        var rate = selected ?? fallback;
        return (rate.MakerBps, rate.TakerBps);
    }

    public static TieredFeeSchedule BinanceFuturesVIP() => new()
    {
        Tiers =
        [
            new(new Money(0, Currency.USD), 2m, 4m),
            new(new Money(10_000_000, Currency.USD), 1.6m, 3.6m),
            new(new Money(50_000_000, Currency.USD), 1.4m, 3.4m),
            new(new Money(100_000_000, Currency.USD), 1.2m, 3.2m),
        ]
    };
}

public sealed record FeeTier(Money MinVolume, decimal MakerBps, decimal TakerBps);
