using System.Globalization;

namespace Rhodium.Primitives;

/// <summary>
/// Base interface for all commands.
/// </summary>
public interface ICommand;

/// <summary>
/// Order type.
/// </summary>
public enum OrderType : byte
{
    Market = 1,
    Limit = 2,
    StopMarket = 3,
    StopLimit = 4,

    /// <summary>
    /// Market order when price touches trigger (buy below market, sell above market).
    /// Opposite of stop order - triggers when price moves favorably.
    /// </summary>
    MarketIfTouched = 5,

    /// <summary>
    /// Limit order when price touches trigger.
    /// </summary>
    LimitIfTouched = 6,

    /// <summary>
    /// Market order that converts to limit at execution price.
    /// Provides price improvement while ensuring execution.
    /// </summary>
    MarketToLimit = 7,

    /// <summary>
    /// Stop that trails price by offset, triggers market order when hit.
    /// </summary>
    TrailingStopMarket = 8,

    /// <summary>
    /// Stop that trails price by offset, triggers limit order when hit.
    /// </summary>
    TrailingStopLimit = 9
}

/// <summary>
/// Time in force.
/// </summary>
public enum TimeInForce : byte
{
    Day = 1,           // Cancel at end of day
    GTC = 2,           // Good til cancelled
    IOC = 3,           // Immediate or cancel
    FOK = 4,           // Fill or kill
    GTD = 5            // Good til date
}

public enum ExecutionLimitPriceMode : byte
{
    None = 0,
    Explicit = 1,
    Bid = 2,
    Ask = 3,
    Mid = 4
}

public enum ExecutionAlgorithm : byte
{
    None = 0,
    Twap = 1,
    Vwap = 2,
    Pov = 3
}

public readonly struct ExecutionSpec
{
    public static ExecutionSpec Market => new(OrderType.Market);

    public ExecutionSpec(
        OrderType orderType,
        Price? limitPrice = null,
        ExecutionLimitPriceMode limitPriceMode = ExecutionLimitPriceMode.None,
        TimeInForce timeInForce = TimeInForce.Day,
        bool postOnly = false,
        int maxSlippageTicks = 0,
        ExecutionAlgorithm algorithm = ExecutionAlgorithm.None,
        Duration horizon = default,
        Duration interval = default,
        decimal participationRate = 0m,
        Price? stopPrice = null,
        Instant? goodTilDate = null,
        Qty? displayQuantity = null)
    {
        OrderType = orderType;
        LimitPrice = limitPrice;
        LimitPriceMode = limitPriceMode;
        TimeInForce = timeInForce;
        PostOnly = postOnly;
        MaxSlippageTicks = maxSlippageTicks;
        Algorithm = algorithm;
        Horizon = horizon;
        Interval = interval;
        ParticipationRate = participationRate;
        StopPrice = stopPrice;
        GoodTilDate = goodTilDate;
        DisplayQuantity = displayQuantity;
    }

    public OrderType OrderType { get; }
    public Price? LimitPrice { get; }
    public ExecutionLimitPriceMode LimitPriceMode { get; }
    public TimeInForce TimeInForce { get; }
    public bool PostOnly { get; }
    public int MaxSlippageTicks { get; }
    public ExecutionAlgorithm Algorithm { get; }
    public Duration Horizon { get; }
    public Duration Interval { get; }
    public decimal ParticipationRate { get; }
    public Price? StopPrice { get; }
    public Instant? GoodTilDate { get; }
    public Qty? DisplayQuantity { get; }

    public ExecutionSpec AtBid() => WithLimitMode(ExecutionLimitPriceMode.Bid);
    public ExecutionSpec AtAsk() => WithLimitMode(ExecutionLimitPriceMode.Ask);
    public ExecutionSpec AtMid() => WithLimitMode(ExecutionLimitPriceMode.Mid);
    public ExecutionSpec At(Price price) => new(OrderType.Limit, price, ExecutionLimitPriceMode.Explicit, TimeInForce, PostOnly, MaxSlippageTicks, Algorithm, Horizon, Interval, ParticipationRate, StopPrice, GoodTilDate, DisplayQuantity);
    public ExecutionSpec GoodTilCancelled() => new(OrderType, LimitPrice, LimitPriceMode, TimeInForce.GTC, PostOnly, MaxSlippageTicks, Algorithm, Horizon, Interval, ParticipationRate, StopPrice, GoodTilDate, DisplayQuantity);
    public ExecutionSpec GoodTil(Instant time) => new(OrderType, LimitPrice, LimitPriceMode, TimeInForce.GTD, PostOnly, MaxSlippageTicks, Algorithm, Horizon, Interval, ParticipationRate, StopPrice, time, DisplayQuantity);
    public ExecutionSpec ImmediateOrCancel() => new(OrderType, LimitPrice, LimitPriceMode, TimeInForce.IOC, PostOnly, MaxSlippageTicks, Algorithm, Horizon, Interval, ParticipationRate, StopPrice, GoodTilDate, DisplayQuantity);
    public ExecutionSpec WithPostOnly() => new(OrderType, LimitPrice, LimitPriceMode, TimeInForce, postOnly: true, MaxSlippageTicks, Algorithm, Horizon, Interval, ParticipationRate, StopPrice, GoodTilDate, DisplayQuantity);
    public ExecutionSpec WithMaxSlippageTicks(int ticks) => new(OrderType, LimitPrice, LimitPriceMode, TimeInForce, PostOnly, ticks, Algorithm, Horizon, Interval, ParticipationRate, StopPrice, GoodTilDate, DisplayQuantity);
    public ExecutionSpec Over(Duration horizon) => new(OrderType, LimitPrice, LimitPriceMode, TimeInForce, PostOnly, MaxSlippageTicks, Algorithm, horizon, Interval, ParticipationRate, StopPrice, GoodTilDate, DisplayQuantity);
    public ExecutionSpec Every(Duration interval) => new(OrderType, LimitPrice, LimitPriceMode, TimeInForce, PostOnly, MaxSlippageTicks, Algorithm, Horizon, interval, ParticipationRate, StopPrice, GoodTilDate, DisplayQuantity);
    public ExecutionSpec MaxParticipation(decimal rate) => new(OrderType, LimitPrice, LimitPriceMode, TimeInForce, PostOnly, MaxSlippageTicks, Algorithm, Horizon, Interval, rate, StopPrice, GoodTilDate, DisplayQuantity);
    public ExecutionSpec Display(Qty quantity) => new(OrderType, LimitPrice, LimitPriceMode, TimeInForce, PostOnly, MaxSlippageTicks, Algorithm, Horizon, Interval, ParticipationRate, StopPrice, GoodTilDate, quantity);

    private ExecutionSpec WithLimitMode(ExecutionLimitPriceMode mode)
        => new(OrderType.Limit, LimitPrice, mode, TimeInForce, PostOnly, MaxSlippageTicks, Algorithm, Horizon, Interval, ParticipationRate, StopPrice, GoodTilDate, DisplayQuantity);
}

public static class Execution
{
    public static ExecutionSpec Market() => ExecutionSpec.Market;
    public static ExecutionSpec Limit() => new(OrderType.Limit);
    public static ExecutionSpec StopMarket(Price stopPrice) => new(OrderType.StopMarket, stopPrice: stopPrice);
    public static ExecutionSpec StopLimit(Price stopPrice, Price limitPrice) =>
        new(OrderType.StopLimit, limitPrice, ExecutionLimitPriceMode.Explicit, stopPrice: stopPrice);
    public static ExecutionSpec Twap() => new(OrderType.Market, algorithm: ExecutionAlgorithm.Twap);
    public static ExecutionSpec Vwap() => new(OrderType.Market, algorithm: ExecutionAlgorithm.Vwap);
    public static ExecutionSpec Pov(decimal participationRate = 0.1m) =>
        new(OrderType.Market, algorithm: ExecutionAlgorithm.Pov, participationRate: participationRate);
}

public readonly record struct OrderIntent(
    StrategyId StrategyId,
    AssetId AssetId,
    Side Side,
    Qty Quantity,
    ExecutionSpec Execution);

// ==================== ACCOUNT TRANSFER COMMANDS ====================

/// <summary>
/// Replay account or custody transfer command.
/// Cash transfer types use <see cref="CashAmount"/>. Asset transfer types use <see cref="Instrument"/> and <see cref="Quantity"/>.
/// </summary>
public readonly record struct AccountTransferCommand(
    AccountTransferId TransferId,
    StrategyId StrategyId,
    int VariantId,
    AccountTransferType TransferType,
    Money? CashAmount = null,
    Instrument? Instrument = null,
    Qty Quantity = default,
    Price? CarryingPrice = null,
    string? ExternalReference = null,
    StrategyId? DestinationStrategyId = null,
    int DestinationVariantId = 0
) : ICommand
{
    public static AccountTransferCommand CashDeposit(
        StrategyId strategyId,
        Money amount,
        int variantId = 0,
        string? externalReference = null)
        => new(
            AccountTransferId.New(),
            strategyId,
            variantId,
            AccountTransferType.CashDeposit,
            CashAmount: amount,
            ExternalReference: externalReference);

    public static AccountTransferCommand CashWithdrawal(
        StrategyId strategyId,
        Money amount,
        int variantId = 0,
        string? externalReference = null)
        => new(
            AccountTransferId.New(),
            strategyId,
            variantId,
            AccountTransferType.CashWithdrawal,
            CashAmount: amount,
            ExternalReference: externalReference);

    public static AccountTransferCommand AssetDeposit(
        StrategyId strategyId,
        Instrument instrument,
        Qty quantity,
        Price carryingPrice,
        int variantId = 0,
        string? externalReference = null)
        => new(
            AccountTransferId.New(),
            strategyId,
            variantId,
            AccountTransferType.AssetDeposit,
            Instrument: instrument,
            Quantity: quantity,
            CarryingPrice: carryingPrice,
            ExternalReference: externalReference);

    public static AccountTransferCommand AssetWithdrawal(
        StrategyId strategyId,
        Instrument instrument,
        Qty quantity,
        Price carryingPrice,
        int variantId = 0,
        string? externalReference = null)
        => new(
            AccountTransferId.New(),
            strategyId,
            variantId,
            AccountTransferType.AssetWithdrawal,
            Instrument: instrument,
            Quantity: quantity,
            CarryingPrice: carryingPrice,
            ExternalReference: externalReference);

    public static AccountTransferCommand InternalCashTransfer(
        StrategyId sourceStrategyId,
        StrategyId destinationStrategyId,
        Money amount,
        int sourceVariantId = 0,
        int destinationVariantId = 0,
        string? externalReference = null)
        => new(
            AccountTransferId.New(),
            sourceStrategyId,
            sourceVariantId,
            AccountTransferType.InternalTransfer,
            CashAmount: amount,
            ExternalReference: externalReference,
            DestinationStrategyId: destinationStrategyId,
            DestinationVariantId: destinationVariantId);

    public static AccountTransferCommand InternalAssetTransfer(
        StrategyId sourceStrategyId,
        StrategyId destinationStrategyId,
        Instrument instrument,
        Qty quantity,
        Price carryingPrice,
        int sourceVariantId = 0,
        int destinationVariantId = 0,
        string? externalReference = null)
        => new(
            AccountTransferId.New(),
            sourceStrategyId,
            sourceVariantId,
            AccountTransferType.InternalTransfer,
            Instrument: instrument,
            Quantity: quantity,
            CarryingPrice: carryingPrice,
            ExternalReference: externalReference,
            DestinationStrategyId: destinationStrategyId,
            DestinationVariantId: destinationVariantId);
}

// ==================== CORPORATE ACTION COMMANDS ====================

/// <summary>
/// Replay corporate action command.
/// Stock splits use <see cref="SplitRatio"/>. Cash dividends use <see cref="DividendPerShare"/>.
/// </summary>
public readonly record struct CorporateActionCommand(
    CorporateActionId CorporateActionId,
    CorporateActionType ActionType,
    Instrument Instrument,
    Instant EffectiveAt,
    decimal SplitRatio = 0m,
    Money? DividendPerShare = null,
    string? ExternalReference = null
) : ICommand
{
    public static CorporateActionCommand StockSplit(
        Instrument instrument,
        decimal splitRatio,
        Instant effectiveAt = default,
        string? externalReference = null)
    {
        if (splitRatio <= 0m)
            throw new ArgumentOutOfRangeException(nameof(splitRatio), "Split ratio must be positive.");

        return new(
            CorporateActionId.New(),
            CorporateActionType.StockSplit,
            instrument,
            effectiveAt,
            SplitRatio: splitRatio,
            ExternalReference: externalReference);
    }

    public static CorporateActionCommand CashDividend(
        Instrument instrument,
        Money dividendPerShare,
        Instant effectiveAt = default,
        string? externalReference = null)
    {
        if (dividendPerShare.Amount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(dividendPerShare), "Dividend per share must be positive.");

        return new(
            CorporateActionId.New(),
            CorporateActionType.CashDividend,
            instrument,
            effectiveAt,
            DividendPerShare: dividendPerShare,
            ExternalReference: externalReference);
    }
}

// ==================== FINANCING CHARGE COMMANDS ====================

/// <summary>
/// Replay financing cash-flow command.
/// Cash interest, borrow fees, perpetual funding, and forex rollover are external account cash flows.
/// Positive <see cref="Amount"/> credits cash; negative <see cref="Amount"/> debits cash.
/// </summary>
public readonly record struct FinancingChargeCommand(
    FinancingChargeId FinancingChargeId,
    FinancingChargeType ChargeType,
    StrategyId StrategyId,
    int VariantId,
    Money Amount,
    Instant EffectiveAt = default,
    Instrument? Instrument = null,
    Qty Quantity = default,
    decimal Rate = 0m,
    string? ExternalReference = null
) : ICommand
{
    public static FinancingChargeCommand CashInterestCredit(
        StrategyId strategyId,
        Money amount,
        int variantId = 0,
        Instant effectiveAt = default,
        decimal rate = 0m,
        string? externalReference = null)
    {
        ValidatePositiveAmount(amount, nameof(amount));
        return New(
            FinancingChargeType.CashInterestCredit,
            strategyId,
            variantId,
            amount,
            effectiveAt,
            rate: rate,
            externalReference: externalReference);
    }

    public static FinancingChargeCommand CashInterestDebit(
        StrategyId strategyId,
        Money amount,
        int variantId = 0,
        Instant effectiveAt = default,
        decimal rate = 0m,
        string? externalReference = null)
    {
        ValidatePositiveAmount(amount, nameof(amount));
        return New(
            FinancingChargeType.CashInterestDebit,
            strategyId,
            variantId,
            new Money(-amount.Amount, amount.Currency),
            effectiveAt,
            rate: rate,
            externalReference: externalReference);
    }

    public static FinancingChargeCommand BorrowFee(
        StrategyId strategyId,
        Instrument instrument,
        Money amount,
        Qty quantity = default,
        int variantId = 0,
        Instant effectiveAt = default,
        decimal rate = 0m,
        string? externalReference = null)
    {
        ValidatePositiveAmount(amount, nameof(amount));
        return New(
            FinancingChargeType.BorrowFee,
            strategyId,
            variantId,
            new Money(-amount.Amount, amount.Currency),
            effectiveAt,
            instrument,
            quantity,
            rate,
            externalReference);
    }

    public static FinancingChargeCommand PerpetualFunding(
        StrategyId strategyId,
        Instrument instrument,
        Money amount,
        Qty quantity = default,
        int variantId = 0,
        Instant effectiveAt = default,
        decimal rate = 0m,
        string? externalReference = null)
        => New(
            FinancingChargeType.PerpetualFunding,
            strategyId,
            variantId,
            amount,
            effectiveAt,
            instrument,
            quantity,
            rate,
            externalReference);

    public static FinancingChargeCommand ForexRollover(
        StrategyId strategyId,
        Instrument instrument,
        Money amount,
        Qty quantity = default,
        int variantId = 0,
        Instant effectiveAt = default,
        decimal rate = 0m,
        string? externalReference = null)
        => New(
            FinancingChargeType.ForexRollover,
            strategyId,
            variantId,
            amount,
            effectiveAt,
            instrument,
            quantity,
            rate,
            externalReference);

    private static FinancingChargeCommand New(
        FinancingChargeType chargeType,
        StrategyId strategyId,
        int variantId,
        Money amount,
        Instant effectiveAt,
        Instrument? instrument = null,
        Qty quantity = default,
        decimal rate = 0m,
        string? externalReference = null)
    {
        if (amount.Amount == 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), "Financing charge amount cannot be zero.");

        return new(
            FinancingChargeId.New(),
            chargeType,
            strategyId,
            variantId,
            amount,
            effectiveAt,
            instrument,
            quantity,
            rate,
            externalReference);
    }

    private static void ValidatePositiveAmount(Money amount, string paramName)
    {
        if (amount.Amount <= 0m)
            throw new ArgumentOutOfRangeException(paramName, "Amount must be positive.");
    }
}

// ==================== ORDER COMMANDS ====================

/// <summary>
/// Submit a new order.
/// </summary>
public readonly record struct SubmitOrder(
    OrderId OrderId,
    StrategyId StrategyId,
    Instrument Instrument,
    Side Side,
    Qty Quantity,
    OrderType Type,
    Price? LimitPrice = null,
    Price? StopPrice = null,
    TimeInForce TimeInForce = TimeInForce.Day,
    int VariantId = 0,
    long NumericTag = 0,
    decimal? TrailingOffset = null,
    TrailingOffsetType? TrailingOffsetType = null,
    Instant? GoodTilDate = null,
    OrderListId? OrderListId = null,
    ContingencyType? ContingencyType = null,
    string? ExecAlgorithmId = null,
    IReadOnlyDictionary<string, string>? ExecAlgorithmParams = null,
    bool PostOnly = false,
    int MaxSlippageTicks = 0,
    Qty? DisplayQuantity = null
) : ICommand
{
    // ==================== FACTORY METHODS ====================

    public static SubmitOrder Market(StrategyId strategyId, Instrument inst, Side side, Qty qty, int variantId = 0, long numericTag = 0) =>
        new(OrderId.New(), strategyId, inst, side, qty, OrderType.Market, VariantId: variantId, NumericTag: numericTag);

    public static SubmitOrder Limit(StrategyId strategyId, Instrument inst, Side side, Qty qty, Price limit, int variantId = 0, long numericTag = 0) =>
        new(OrderId.New(), strategyId, inst, side, qty, OrderType.Limit, LimitPrice: limit, VariantId: variantId, NumericTag: numericTag);

    public static SubmitOrder IcebergLimit(StrategyId strategyId, Instrument inst, Side side, Qty qty, Price limit, Qty displayQuantity, int variantId = 0, long numericTag = 0) =>
        new(OrderId.New(), strategyId, inst, side, qty, OrderType.Limit, LimitPrice: limit, VariantId: variantId, NumericTag: numericTag, DisplayQuantity: displayQuantity);

    public static SubmitOrder StopMarket(StrategyId strategyId, Instrument inst, Side side, Qty qty, Price stop, int variantId = 0, long numericTag = 0) =>
        new(OrderId.New(), strategyId, inst, side, qty, OrderType.StopMarket, StopPrice: stop, VariantId: variantId, NumericTag: numericTag);

    public static SubmitOrder StopLimit(StrategyId strategyId, Instrument inst, Side side, Qty qty, Price stop, Price limit, int variantId = 0, long numericTag = 0) =>
        new(OrderId.New(), strategyId, inst, side, qty, OrderType.StopLimit, StopPrice: stop, LimitPrice: limit, VariantId: variantId, NumericTag: numericTag);

    // Convenience
    public static SubmitOrder Buy(StrategyId strategyId, Instrument inst, Qty qty, int variantId = 0, long numericTag = 0) =>
        Market(strategyId, inst, Side.Buy, qty, variantId, numericTag);
    public static SubmitOrder Sell(StrategyId strategyId, Instrument inst, Qty qty, int variantId = 0, long numericTag = 0) =>
        Market(strategyId, inst, Side.Sell, qty, variantId, numericTag);
    public static SubmitOrder BuyLimit(StrategyId strategyId, Instrument inst, Qty qty, Price limit, int variantId = 0, long numericTag = 0) =>
        Limit(strategyId, inst, Side.Buy, qty, limit, variantId, numericTag);
    public static SubmitOrder SellLimit(StrategyId strategyId, Instrument inst, Qty qty, Price limit, int variantId = 0, long numericTag = 0) =>
        Limit(strategyId, inst, Side.Sell, qty, limit, variantId, numericTag);

    // ==================== ADDITIONAL FACTORY METHODS ====================

    /// <summary>Create a trailing stop market order.</summary>
    public static SubmitOrder TrailingStop(
        StrategyId strategyId,
        Instrument inst,
        Side side,
        Qty qty,
        decimal offset,
        TrailingOffsetType offsetType = (TrailingOffsetType)1,
        TimeInForce tif = TimeInForce.GTC,
        int variantId = 0,
        long numericTag = 0) =>
        new(OrderId.New(), strategyId, inst, side, qty, OrderType.TrailingStopMarket,
            TimeInForce: tif, VariantId: variantId, NumericTag: numericTag,
            TrailingOffset: offset, TrailingOffsetType: offsetType);

    /// <summary>Create a trailing stop limit order.</summary>
    public static SubmitOrder TrailingStopLimit(
        StrategyId strategyId,
        Instrument inst,
        Side side,
        Qty qty,
        decimal trailingOffset,
        TrailingOffsetType offsetType,
        Price limitOffset,
        TimeInForce tif = TimeInForce.GTC,
        int variantId = 0,
        long numericTag = 0) =>
        new(OrderId.New(), strategyId, inst, side, qty, OrderType.TrailingStopLimit,
            LimitPrice: limitOffset, TimeInForce: tif, VariantId: variantId, NumericTag: numericTag,
            TrailingOffset: trailingOffset, TrailingOffsetType: offsetType);

    /// <summary>Create a market-if-touched order.</summary>
    public static SubmitOrder MarketIfTouched(
        StrategyId strategyId,
        Instrument inst,
        Side side,
        Qty qty,
        Price triggerPrice,
        int variantId = 0,
        long numericTag = 0) =>
        new(OrderId.New(), strategyId, inst, side, qty, OrderType.MarketIfTouched,
            StopPrice: triggerPrice, VariantId: variantId, NumericTag: numericTag);

    /// <summary>Create a limit-if-touched order.</summary>
    public static SubmitOrder LimitIfTouched(
        StrategyId strategyId,
        Instrument inst,
        Side side,
        Qty qty,
        Price triggerPrice,
        Price limitPrice,
        int variantId = 0,
        long numericTag = 0) =>
        new(OrderId.New(), strategyId, inst, side, qty, OrderType.LimitIfTouched,
            StopPrice: triggerPrice, LimitPrice: limitPrice, VariantId: variantId, NumericTag: numericTag);

    /// <summary>Create an order routed to execution algorithm.</summary>
    public static SubmitOrder WithAlgorithm(
        StrategyId strategyId,
        Instrument inst,
        Side side,
        Qty qty,
        string algorithmId,
        IReadOnlyDictionary<string, string> algoParams,
        int variantId = 0,
        long numericTag = 0) =>
        new(OrderId.New(), strategyId, inst, side, qty, OrderType.Market,
            VariantId: variantId, NumericTag: numericTag,
            ExecAlgorithmId: algorithmId, ExecAlgorithmParams: algoParams);

    /// <summary>Create a TWAP order.</summary>
    public static SubmitOrder Twap(
        StrategyId strategyId,
        Instrument inst,
        Side side,
        Qty qty,
        TimeSpan horizon,
        TimeSpan interval,
        int variantId = 0,
        long numericTag = 0) =>
        WithAlgorithm(strategyId, inst, side, qty, "TWAP",
            new Dictionary<string, string>
            {
                ["horizon_secs"] = ((int)horizon.TotalSeconds).ToString(),
                ["interval_secs"] = ((int)interval.TotalSeconds).ToString()
            }, variantId: variantId, numericTag: numericTag);

    /// <summary>Create a VWAP order.</summary>
    public static SubmitOrder Vwap(
        StrategyId strategyId,
        Instrument inst,
        Side side,
        Qty qty,
        TimeSpan horizon,
        decimal participationRate = 0.1m,
        int variantId = 0,
        long numericTag = 0) =>
        WithAlgorithm(strategyId, inst, side, qty, "VWAP",
            new Dictionary<string, string>
            {
                ["horizon_secs"] = ((int)horizon.TotalSeconds).ToString(),
                ["participation_rate"] = participationRate.ToString(CultureInfo.InvariantCulture)
            }, variantId: variantId, numericTag: numericTag);

    /// <summary>Create a POV order that participates in replay market volume until filled or cancelled.</summary>
    public static SubmitOrder Pov(
        StrategyId strategyId,
        Instrument inst,
        Side side,
        Qty qty,
        decimal participationRate,
        TimeSpan? horizon = null,
        int variantId = 0,
        long numericTag = 0)
    {
        var parameters = new Dictionary<string, string>
        {
            ["participation_rate"] = participationRate.ToString(CultureInfo.InvariantCulture)
        };
        if (horizon.HasValue)
            parameters["horizon_secs"] = ((int)horizon.Value.TotalSeconds).ToString(CultureInfo.InvariantCulture);

        return WithAlgorithm(strategyId, inst, side, qty, "POV", parameters, variantId: variantId, numericTag: numericTag);
    }
}

/// <summary>
/// Submit an order list (grouped orders with contingency).
/// </summary>
public readonly record struct SubmitOrderList(
    OrderList OrderList
) : ICommand
{
    public static SubmitOrderList Create(OrderList orderList) => new(orderList);
}

/// <summary>
/// Cancel an existing order.
/// </summary>
public readonly record struct CancelOrder(OrderId OrderId) : ICommand;

/// <summary>
/// Cancel all orders, optionally filtered.
/// </summary>
public readonly record struct CancelAllOrders(
    Instrument? Instrument = null,
    Side? Side = null
) : ICommand;

/// <summary>
/// Modify an existing order.
/// </summary>
public readonly record struct ModifyOrder(
    OrderId OrderId,
    Qty? NewQuantity = null,
    Price? NewLimitPrice = null
) : ICommand;

// ==================== POSITION COMMANDS (High-Level) ====================

/// <summary>
/// Express desired position (framework converts to orders).
/// </summary>
public readonly record struct SetPosition(
    Instrument Instrument,
    Qty TargetQuantity,
    OrderType OrderType = OrderType.Market,
    Price? LimitPrice = null,
    int VariantId = 0,
    long NumericTag = 0
) : ICommand
{
    public static SetPosition Flat(Instrument inst) => new(inst, Qty.Zero);
    public static SetPosition Long(Instrument inst, Qty qty) => new(inst, qty);
    public static SetPosition Short(Instrument inst, Qty qty) => new(inst, -qty);
}

/// <summary>
/// Set position as percentage of portfolio.
/// </summary>
public readonly record struct SetAllocation(
    Instrument Instrument,
    decimal TargetPercent,  // 0.0 to 1.0 (or negative for short)
    OrderType OrderType = OrderType.Market,
    int VariantId = 0,
    long NumericTag = 0
) : ICommand;

/// <summary>
/// Liquidate all positions.
/// </summary>
public readonly record struct LiquidateAll(
    long NumericTag = 0
) : ICommand;
