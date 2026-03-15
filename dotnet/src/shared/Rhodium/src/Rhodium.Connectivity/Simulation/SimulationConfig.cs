using Rhodium.Primitives;

namespace Rhodium.Connectivity.Simulation;

/// <summary>
/// Configuration for replay-based backtesting simulation.
/// Simple parameters, not complex model objects.
/// </summary>
public sealed record SimulationConfig
{
    public required LatencyParams Latency { get; init; }
    public required QueueParams Queue { get; init; }
    public required FeeParams Fees { get; init; }
    public SlippageParams Slippage { get; init; } = SlippageParams.None;
    public FillBehavior FillBehavior { get; init; } = FillBehavior.NoPartialFill;
    public DepthLevel RequiredDepth { get; init; } = DepthLevel.L2_MarketByPrice;

    /// <summary>
    /// Account type for capital constraint simulation.
    /// Cash accounts lock full notional, margin accounts use leverage.
    /// Default: Cash (conservative).
    /// </summary>
    public AccountType AccountType { get; init; } = AccountType.Cash;

    /// <summary>
    /// Initial market status.
    /// If not Open, orders will be rejected during simulation.
    /// Default: Open.
    /// </summary>
    public MarketStatus InitialMarketStatus { get; init; } = MarketStatus.Open;

    /// <summary>
    /// Bar OHLC processing order.
    /// Fixed = always O→H→L→C (50% accuracy).
    /// Adaptive = smart ordering based on bar structure (75-85% accuracy).
    /// Default: Fixed.
    /// </summary>
    public BarOrderingMode BarOrdering { get; init; } = BarOrderingMode.Fixed;

    /// <summary>
    /// Fill model for custom fill logic.
    /// Default: DefaultFillModel (standard price crossing + queue logic).
    /// </summary>
    public IFillModel FillModel { get; init; } = new DefaultFillModel();

    /// <summary>
    /// Deterministic PRNG seed for latency sampling, queue advancement.
    /// Default: hash(batchMapVersion) for deterministic replay.
    /// </summary>
    public int Seed { get; init; } = 0;

    // ==================== PRESETS ====================

    /// <summary>
    /// Realistic preset for liquid crypto futures (Binance/Bybit).
    /// Power quadratic queue, standard maker/taker fees, moderate latency.
    /// </summary>
    public static SimulationConfig CryptoFuturesRealistic() => new()
    {
        Latency = new(Duration.FromMicros(500), Duration.FromMicros(500), StdDevFraction: 0.2),
        Queue = QueueParams.RealisticLiquid(),
        Fees = FeeParams.BinanceFutures(),
        Slippage = SlippageParams.None,
        FillBehavior = FillBehavior.PartialFillOnTrade
    };

    /// <summary>
    /// Conservative preset for risk assessment.
    /// Risk-averse queue, higher fees, higher latency, slippage included.
    /// </summary>
    public static SimulationConfig Conservative() => new()
    {
        Latency = new(Duration.FromMillis(10), Duration.FromMillis(10)),
        Queue = QueueParams.RiskAverse(),
        Fees = new()
        {
            Model = FeeModelType.PercentageOfValue,
            MakerBps = 5m,
            TakerBps = 15m
        },
        Slippage = new(SlippageModelType.VolumeProportional, BpsPerLotSize: 1m),
        FillBehavior = FillBehavior.NoPartialFill
    };

    /// <summary>
    /// Illiquid market preset (altcoins, low-volume pairs).
    /// Cubic queue profile, higher fees, partial fills required.
    /// </summary>
    public static SimulationConfig IlliquidMarket() => new()
    {
        Latency = new(Duration.FromMillis(1), Duration.FromMillis(1), StdDevFraction: 0.3),
        Queue = QueueParams.RealisticIlliquid(),
        Fees = FeeParams.MakerTaker(makerBps: 10m, takerBps: 20m),
        Slippage = new(SlippageModelType.VolumeProportional, BpsPerLotSize: 2m),
        FillBehavior = FillBehavior.PartialFillOnTrade
    };

    /// <summary>
    /// Equity market preset (US stocks).
    /// Fixed fee per trade, moderate queue, no slippage.
    /// </summary>
    public static SimulationConfig USEquities() => new()
    {
        Latency = new(Duration.FromMicros(100), Duration.FromMicros(100), StdDevFraction: 0.15),
        Queue = QueueParams.PowerQuadratic(),
        Fees = FeeParams.Fixed(new Money(0.50m, Currency.USD)),
        Slippage = SlippageParams.None,
        FillBehavior = FillBehavior.NoPartialFill
    };

    /// <summary>
    /// Instant preset: zero latency, always front of queue, no fees.
    /// Use for strategy logic testing only (unrealistic).
    /// </summary>
    public static SimulationConfig Instant() => new()
    {
        Latency = new(Duration.Zero, Duration.Zero),
        Queue = QueueParams.AlwaysFront(),
        Fees = FeeParams.Zero,
        Slippage = SlippageParams.None,
        FillBehavior = FillBehavior.NoPartialFill
    };
}

/// <summary>
/// Market depth level requirement.
/// </summary>
public enum DepthLevel : byte
{
    /// <summary>
    /// Level 1: Top-of-book only (best bid/ask).
    /// Queue simulation only applies when order is at best.
    /// </summary>
    L1_TopOfBook = 1,

    /// <summary>
    /// Level 2: Market-By-Price (full depth).
    /// Queue simulation works at all price levels.
    /// </summary>
    L2_MarketByPrice = 2
}

/// <summary>
/// Account type for capital constraints simulation.
/// Determines how order notional value affects available capital.
/// </summary>
public enum AccountType : byte
{
    /// <summary>
    /// Cash account: Locks full notional value when order is submitted.
    /// No leverage - cannot trade beyond available cash balance.
    /// Common for spot trading (stocks, crypto spot).
    /// </summary>
    Cash = 1,

    /// <summary>
    /// Margin account: Locks margin based on leverage.
    /// Allows leveraged trading - can control larger positions with less capital.
    /// Common for derivatives (futures, options, CFDs).
    /// Note: Margin calculation requires IMarginModel (future specification).
    /// </summary>
    Margin = 2
}

/// <summary>
/// Market trading status.
/// Controls whether orders can be submitted and filled.
/// </summary>
public enum MarketStatus : byte
{
    /// <summary>
    /// Market is in pre-open phase.
    /// Orders may be accepted but not filled.
    /// </summary>
    PreOpen = 1,

    /// <summary>
    /// Market is open for trading.
    /// Orders can be submitted and filled.
    /// </summary>
    Open = 2,

    /// <summary>
    /// Market is closed.
    /// No order submission or fills.
    /// </summary>
    Closed = 3,

    /// <summary>
    /// Trading is temporarily halted.
    /// Pending orders remain but no new fills occur.
    /// Common during circuit breakers or exchange issues.
    /// </summary>
    Halted = 4
}
