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

// ==================== ORDER COMMANDS ====================

/// <summary>
/// Submit a new order.
/// </summary>
public readonly record struct SubmitOrder(
    OrderId OrderId,
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
    OrderListId? OrderListId = null,
    string? ExecAlgorithmId = null,
    IReadOnlyDictionary<string, string>? ExecAlgorithmParams = null
) : ICommand
{
    // ==================== FACTORY METHODS ====================

    public static SubmitOrder Market(Instrument inst, Side side, Qty qty, int variantId = 0, long numericTag = 0) =>
        new(OrderId.New(), inst, side, qty, OrderType.Market, VariantId: variantId, NumericTag: numericTag);

    public static SubmitOrder Limit(Instrument inst, Side side, Qty qty, Price limit, int variantId = 0, long numericTag = 0) =>
        new(OrderId.New(), inst, side, qty, OrderType.Limit, LimitPrice: limit, VariantId: variantId, NumericTag: numericTag);

    public static SubmitOrder StopMarket(Instrument inst, Side side, Qty qty, Price stop, int variantId = 0, long numericTag = 0) =>
        new(OrderId.New(), inst, side, qty, OrderType.StopMarket, StopPrice: stop, VariantId: variantId, NumericTag: numericTag);

    public static SubmitOrder StopLimit(Instrument inst, Side side, Qty qty, Price stop, Price limit, int variantId = 0, long numericTag = 0) =>
        new(OrderId.New(), inst, side, qty, OrderType.StopLimit, StopPrice: stop, LimitPrice: limit, VariantId: variantId, NumericTag: numericTag);

    // Convenience
    public static SubmitOrder Buy(Instrument inst, Qty qty, int variantId = 0, long numericTag = 0) =>
        Market(inst, Side.Buy, qty, variantId, numericTag);
    public static SubmitOrder Sell(Instrument inst, Qty qty, int variantId = 0, long numericTag = 0) =>
        Market(inst, Side.Sell, qty, variantId, numericTag);
    public static SubmitOrder BuyLimit(Instrument inst, Qty qty, Price limit, int variantId = 0, long numericTag = 0) =>
        Limit(inst, Side.Buy, qty, limit, variantId, numericTag);
    public static SubmitOrder SellLimit(Instrument inst, Qty qty, Price limit, int variantId = 0, long numericTag = 0) =>
        Limit(inst, Side.Sell, qty, limit, variantId, numericTag);

    // ==================== ADDITIONAL FACTORY METHODS ====================

    /// <summary>Create a trailing stop market order.</summary>
    public static SubmitOrder TrailingStop(
        Instrument inst,
        Side side,
        Qty qty,
        decimal offset,
        TrailingOffsetType offsetType = (TrailingOffsetType)1,
        TimeInForce tif = TimeInForce.GTC,
        int variantId = 0,
        long numericTag = 0) =>
        new(OrderId.New(), inst, side, qty, OrderType.TrailingStopMarket,
            TimeInForce: tif, VariantId: variantId, NumericTag: numericTag,
            TrailingOffset: offset, TrailingOffsetType: offsetType);

    /// <summary>Create a trailing stop limit order.</summary>
    public static SubmitOrder TrailingStopLimit(
        Instrument inst,
        Side side,
        Qty qty,
        decimal trailingOffset,
        TrailingOffsetType offsetType,
        Price limitOffset,
        TimeInForce tif = TimeInForce.GTC,
        int variantId = 0,
        long numericTag = 0) =>
        new(OrderId.New(), inst, side, qty, OrderType.TrailingStopLimit,
            LimitPrice: limitOffset, TimeInForce: tif, VariantId: variantId, NumericTag: numericTag,
            TrailingOffset: trailingOffset, TrailingOffsetType: offsetType);

    /// <summary>Create a market-if-touched order.</summary>
    public static SubmitOrder MarketIfTouched(
        Instrument inst,
        Side side,
        Qty qty,
        Price triggerPrice,
        int variantId = 0,
        long numericTag = 0) =>
        new(OrderId.New(), inst, side, qty, OrderType.MarketIfTouched,
            StopPrice: triggerPrice, VariantId: variantId, NumericTag: numericTag);

    /// <summary>Create a limit-if-touched order.</summary>
    public static SubmitOrder LimitIfTouched(
        Instrument inst,
        Side side,
        Qty qty,
        Price triggerPrice,
        Price limitPrice,
        int variantId = 0,
        long numericTag = 0) =>
        new(OrderId.New(), inst, side, qty, OrderType.LimitIfTouched,
            StopPrice: triggerPrice, LimitPrice: limitPrice, VariantId: variantId, NumericTag: numericTag);

    /// <summary>Create an order routed to execution algorithm.</summary>
    public static SubmitOrder WithAlgorithm(
        Instrument inst,
        Side side,
        Qty qty,
        string algorithmId,
        IReadOnlyDictionary<string, string> algoParams,
        int variantId = 0,
        long numericTag = 0) =>
        new(OrderId.New(), inst, side, qty, OrderType.Market,
            VariantId: variantId, NumericTag: numericTag,
            ExecAlgorithmId: algorithmId, ExecAlgorithmParams: algoParams);

    /// <summary>Create a TWAP order.</summary>
    public static SubmitOrder Twap(
        Instrument inst,
        Side side,
        Qty qty,
        TimeSpan horizon,
        TimeSpan interval,
        int variantId = 0,
        long numericTag = 0) =>
        WithAlgorithm(inst, side, qty, "TWAP",
            new Dictionary<string, string>
            {
                ["horizon_secs"] = ((int)horizon.TotalSeconds).ToString(),
                ["interval_secs"] = ((int)interval.TotalSeconds).ToString()
            }, variantId: variantId, numericTag: numericTag);

    /// <summary>Create a VWAP order.</summary>
    public static SubmitOrder Vwap(
        Instrument inst,
        Side side,
        Qty qty,
        TimeSpan horizon,
        decimal participationRate = 0.1m,
        int variantId = 0,
        long numericTag = 0) =>
        WithAlgorithm(inst, side, qty, "VWAP",
            new Dictionary<string, string>
            {
                ["horizon_secs"] = ((int)horizon.TotalSeconds).ToString(),
                ["participation_rate"] = participationRate.ToString()
            }, variantId: variantId, numericTag: numericTag);
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
