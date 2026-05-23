namespace Rhodium.Primitives;

/// <summary>
/// Complete state of an order.
/// </summary>
public sealed class Order
{
    /// <summary>Factory for empty/placeholder order state.</summary>
    public static Order Empty(Instrument inst) => new()
    {
        Id = default,
        Instrument = inst,
        Side = Side.None,
        Quantity = Qty.Zero,
        Type = OrderType.Market
    };

    // ==================== IDENTITY ====================
    public required OrderId Id { get; init; }
    public required Instrument Instrument { get; init; }
    public required Side Side { get; init; }
    public required Qty Quantity { get; init; }
    public required OrderType Type { get; init; }

    // ==================== PRICES ====================
    public Price? LimitPrice { get; init; }
    public Price? StopPrice { get; init; }

    // ==================== TIME ====================
    public TimeInForce TimeInForce { get; init; } = TimeInForce.Day;
    public Instant? GoodTilDate { get; init; }

    /// <summary>Links order to a specific parameter set (grid search / variant).</summary>
    public int VariantId { get; init; }

    /// <summary>User-defined numeric tag (signal type, regime id, etc.).</summary>
    public long NumericTag { get; init; }

    // ==================== TIMESTAMPS (HFT) ====================
    /// <summary>When we sent this order (local clock).</summary>
    public Instant LocalTimestamp { get; set; }

    /// <summary>When exchange processed this order (exchange clock).</summary>
    public Instant ExchangeTimestamp { get; set; }

    /// <summary>When we received the response (local clock).</summary>
    public Instant ResponseTimestamp { get; set; }

    // ==================== TICK PRICING (HFT) ====================
    /// <summary>Tick size for this instrument.</summary>
    public decimal TickSize { get; init; } = 0.01m;

    /// <summary>Lot size for this instrument.</summary>
    public decimal LotSize { get; init; } = 1m;

    /// <summary>Limit price in ticks (for HFT).</summary>
    public TickPrice? LimitPriceTick => LimitPrice.HasValue
        ? TickPrice.FromPrice(LimitPrice.Value, TickSize)
        : null;

    // ==================== QUEUE POSITION (HFT) ====================
    /// <summary>Queue position tracking for realistic fill simulation.</summary>
    public QueuePosition? QueuePos { get; set; }

    /// <summary>Whether this order was filled as maker (passive).</summary>
    public bool IsMaker { get; set; }

    // ==================== FILL TRACKING ====================
    public OrderStatus Status { get; internal set; } = OrderStatus.Pending;
    public Qty FilledQty { get; internal set; } = Qty.Zero;
    public Price? AvgFillPrice { get; internal set; }
    public TickPrice? AvgFillPriceTick { get; internal set; }
    public Money TotalCommission { get; internal set; } = Money.Zero(Currency.USD);

    // ==================== TRAILING STOP ====================

    /// <summary>
    /// Trailing stop offset amount.
    /// Interpretation depends on TrailingOffsetType.
    /// </summary>
    public decimal? TrailingOffset { get; init; }

    /// <summary>
    /// How TrailingOffset is interpreted (Price, Ticks, Percent).
    /// Required when TrailingOffset is set.
    /// </summary>
    public TrailingOffsetType? TrailingOffsetType { get; init; }

    // ==================== ORDER LIST ====================

    /// <summary>
    /// ID of the order list this order belongs to, if any.
    /// </summary>
    public OrderListId? OrderListId { get; init; }

    /// <summary>
    /// Contingency type when part of an order list.
    /// </summary>
    public ContingencyType? ContingencyType { get; init; }

    // ==================== DISPLAY / ICEBERG ====================

    /// <summary>
    /// Visible slice for an iceberg-style order. Null means the full remaining quantity is displayed.
    /// </summary>
    public Qty? DisplayQuantity { get; init; }

    // ==================== EXECUTION ALGORITHM ====================

    /// <summary>
    /// Execution algorithm ID (e.g., "TWAP", "VWAP").
    /// When set, order is routed to algorithm instead of venue.
    /// </summary>
    public string? ExecAlgorithmId { get; init; }

    /// <summary>
    /// Parameters for execution algorithm.
    /// Keys and values are algorithm-specific.
    /// </summary>
    public IReadOnlyDictionary<string, string>? ExecAlgorithmParams { get; init; }

    // ==================== DERIVED ====================
    public Qty RemainingQty => Quantity - FilledQty;
    public bool IsOpen => Status is OrderStatus.Pending or OrderStatus.Open or OrderStatus.PartiallyFilled;
    public bool IsClosed => !IsOpen;
    public decimal FillPercent => Quantity.Value > 0 ? FilledQty.Value / Quantity.Value : 0m;

    /// <summary>Entry latency (local → exchange).</summary>
    public Duration EntryLatency => new(ExchangeTimestamp.Nanos - LocalTimestamp.Nanos);

    /// <summary>Response latency (exchange → local).</summary>
    public Duration ResponseLatency => new(ResponseTimestamp.Nanos - ExchangeTimestamp.Nanos);

    /// <summary>Round-trip latency.</summary>
    public Duration RoundTrip => new(ResponseTimestamp.Nanos - LocalTimestamp.Nanos);

    public bool IsTrailingStop => Type is OrderType.TrailingStopMarket or OrderType.TrailingStopLimit;
    public bool IsPartOfOrderList => OrderListId.HasValue;
    public bool UsesExecAlgorithm => ExecAlgorithmId != null;

    // ==================== STATE TRANSITIONS ====================
    public void Accept(Instant exchTime)
    {
        ExchangeTimestamp = exchTime;
        Status = OrderStatus.Open;
    }

    public void Reject(Instant exchTime)
    {
        ExchangeTimestamp = exchTime;
        Status = OrderStatus.Rejected;
    }

    public void Fill(Qty qty, Price price, Money commission, Instant exchTime, bool isMaker = false)
    {
        ExchangeTimestamp = exchTime;
        IsMaker = isMaker;

        var newFilledQty = FilledQty + qty;
        AvgFillPrice = AvgFillPrice.HasValue
            ? new Price((AvgFillPrice.Value.Value * FilledQty.Value + price.Value * qty.Value) / newFilledQty.Value)
            : price;
        AvgFillPriceTick = TickPrice.FromPrice(AvgFillPrice.Value, TickSize);
        FilledQty = newFilledQty;
        TotalCommission = TotalCommission + commission;
        Status = newFilledQty >= Quantity ? OrderStatus.Filled : OrderStatus.PartiallyFilled;
    }

    public void Cancel(Instant exchTime)
    {
        ExchangeTimestamp = exchTime;
        Status = OrderStatus.Cancelled;
    }

    public void Expire(Instant exchTime)
    {
        ExchangeTimestamp = exchTime;
        Status = OrderStatus.Expired;
    }
}
