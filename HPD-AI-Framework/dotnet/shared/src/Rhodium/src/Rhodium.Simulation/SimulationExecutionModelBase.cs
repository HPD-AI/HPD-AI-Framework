using Rhodium.Events;
using Rhodium.Kernel;
using Rhodium.Primitives;

namespace Rhodium.Simulation;

public abstract class SimulationExecutionModelBase : ISimulationExecutionModel
{
    private readonly List<ExecutionEvent> _executionEvents = [];
    private readonly List<PendingOrder> _pendingOrders = [];
    private readonly List<int> _completedOrderIndexes = [];
    private long _nextOrderId = 1;

    protected RhodiumRuntime? Runtime { get; private set; }
    protected SimulationConfig Config { get; private set; } = SimulationConfig.Queue();

    public virtual void Initialize(in SimulationExecutionContext context)
    {
        Runtime = context.Runtime;
        Config = context.Config;
        _executionEvents.Clear();
        _pendingOrders.Clear();
        _completedOrderIndexes.Clear();
        _nextOrderId = 1;
    }

    public abstract void OnMarketEvent(FinanceEvent evt, in MarketKernel market);

    public abstract void Submit(in OrderIntent intent, in MarketKernel market);

    public int DrainExecutionEvents(Span<ExecutionEvent> destination)
    {
        var count = Math.Min(destination.Length, _executionEvents.Count);
        for (var i = 0; i < count; i++)
            destination[i] = _executionEvents[i];

        _executionEvents.RemoveRange(0, count);
        return count;
    }

    protected void Accept(in SimulatedOrder order)
        => _executionEvents.Add(new OrderAccepted(order.OrderId, order.Intent.StrategyId, order.VariantId));

    protected void Reject(in SimulatedOrder order, string reason)
        => _executionEvents.Add(new OrderRejected(order.OrderId, order.Intent.StrategyId, order.VariantId, reason));

    protected bool RejectIfMarketNotOpen(in SimulatedOrder order)
    {
        if (Config.InitialMarketStatus == MarketStatus.Open)
            return false;

        Reject(in order, $"Market is {Config.InitialMarketStatus}; simulated order submission is disabled.");
        return true;
    }

    protected void Fill(in SimulatedOrder order, Qty quantity, Price price, bool isMaker)
    {
        var improvedPrice = Config.PriceImprovement.Apply(price, order.Intent.Side, isMaker);
        var fillPrice = Config.Slippage.Apply(improvedPrice, quantity, order.Intent.Side);
        var commission = Config.Fees.Calculate(quantity, fillPrice, order.Intent.Side, isMaker);
        _executionEvents.Add(new OrderFilled(
            order.OrderId,
            order.Instrument,
            order.VariantId,
            order.Intent.StrategyId,
            order.Intent.Side,
            quantity,
            fillPrice,
            commission));
    }

    protected SimulatedOrder CreateOrder(in OrderIntent intent)
    {
        var runtime = Runtime ?? throw new InvalidOperationException("Execution model is not initialized.");
        var (instrument, variantId) = runtime.BatchMap.GetContext(intent.AssetId.VirtualIndex);
        return new SimulatedOrder(
            new OrderId(_nextOrderId++),
            intent,
            instrument,
            variantId);
    }

    protected bool TryResolveLimitPrice(in SimulatedOrder order, in MarketKernel market, out Price price)
    {
        var execution = order.Intent.Execution;
        if (execution.LimitPrice.HasValue)
        {
            price = execution.LimitPrice.Value;
            return true;
        }

        var metadata = market.GetMetadata(order.Intent.AssetId);
        price = execution.LimitPriceMode switch
        {
            ExecutionLimitPriceMode.Bid when market.GetBestBidTick(order.Intent.AssetId) is { } bid =>
                new Price(bid * metadata.TickSize, metadata.Currency),
            ExecutionLimitPriceMode.Ask when market.GetBestAskTick(order.Intent.AssetId) is { } ask =>
                new Price(ask * metadata.TickSize, metadata.Currency),
            ExecutionLimitPriceMode.Mid when market.GetBestBidTick(order.Intent.AssetId) is { } bid
                && market.GetBestAskTick(order.Intent.AssetId) is { } ask =>
                new Price(((bid + ask) * metadata.TickSize) / 2m, metadata.Currency),
            _ => default
        };

        return price != default;
    }

    protected static bool TryGetMarketPrice(FinanceEvent evt, Side side, out Price price)
    {
        switch (evt)
        {
            case BarClosed bar:
                price = bar.Bar.Close;
                return true;
            case QuoteReceived quote:
                price = side == Side.Buy ? quote.Quote.Ask : quote.Quote.Bid;
                return price.Value > 0m;
            case TradeOccurred trade:
                price = trade.Trade.Price;
                return true;
            default:
                price = default;
                return false;
        }
    }

    protected bool ShouldFillOnEvent(
        in SimulatedOrder order,
        Price? limitPrice,
        bool stopTriggeredBeforeEvent,
        FinanceEvent evt,
        out Price fillPrice,
        out bool stopTriggeredAfterEvent)
    {
        stopTriggeredAfterEvent = stopTriggeredBeforeEvent;

        if (order.Intent.Execution.OrderType == OrderType.Market)
            return TryGetMarketPrice(evt, order.Intent.Side, out fillPrice);

        if (order.Intent.Execution.OrderType is OrderType.StopMarket or OrderType.StopLimit)
            return ShouldFillStopOnEvent(
                in order,
                limitPrice,
                stopTriggeredBeforeEvent,
                evt,
                out fillPrice,
                out stopTriggeredAfterEvent);

        if (!limitPrice.HasValue)
        {
            fillPrice = default;
            return false;
        }

        var limit = limitPrice.Value;
        switch (evt)
        {
            case BarClosed bar:
                var touched = order.Intent.Side == Side.Buy
                    ? bar.Bar.Low.Value <= limit.Value
                    : bar.Bar.High.Value >= limit.Value;
                fillPrice = limit;
                return touched;
            case QuoteReceived quote:
                var quoteTouched = order.Intent.Side == Side.Buy
                    ? quote.Quote.Ask.Value <= limit.Value
                    : quote.Quote.Bid.Value >= limit.Value;
                fillPrice = limit;
                return quoteTouched;
            case TradeOccurred trade:
                var tradeTouched = order.Intent.Side == Side.Buy
                    ? trade.Trade.Price.Value <= limit.Value
                    : trade.Trade.Price.Value >= limit.Value;
                fillPrice = limit;
                return tradeTouched;
            default:
                fillPrice = default;
                return false;
        }
    }

    protected bool ShouldFillStopOnEvent(
        in SimulatedOrder order,
        Price? limitPrice,
        bool stopTriggeredBeforeEvent,
        FinanceEvent evt,
        out Price fillPrice,
        out bool stopTriggeredAfterEvent)
    {
        stopTriggeredAfterEvent = stopTriggeredBeforeEvent;
        var stopPrice = order.Intent.Execution.StopPrice;
        if (!stopPrice.HasValue)
        {
            fillPrice = default;
            return false;
        }

        var triggeredThisEvent = IsStopTriggered(order.Intent.Side, stopPrice.Value, evt);
        stopTriggeredAfterEvent = stopTriggeredBeforeEvent || triggeredThisEvent;
        if (!stopTriggeredAfterEvent)
        {
            fillPrice = default;
            return false;
        }

        if (order.Intent.Execution.OrderType == OrderType.StopMarket)
        {
            fillPrice = stopPrice.Value;
            return true;
        }

        if (!limitPrice.HasValue)
        {
            fillPrice = default;
            return false;
        }

        if (evt is BarClosed bar && !stopTriggeredBeforeEvent)
        {
            var orderedTouch = IsStopLimitTouchedInBarOrder(
                order.Intent.Side,
                stopPrice.Value,
                limitPrice.Value,
                bar.Bar);
            fillPrice = limitPrice.Value;
            return orderedTouch;
        }

        return IsLimitTouched(order.Intent.Side, limitPrice.Value, evt, out fillPrice);
    }

    protected static bool IsStopTriggered(Side side, Price stopPrice, FinanceEvent evt)
    {
        return evt switch
        {
            BarClosed bar => side == Side.Buy
                ? bar.Bar.High.Value >= stopPrice.Value
                : bar.Bar.Low.Value <= stopPrice.Value,
            QuoteReceived quote => side == Side.Buy
                ? quote.Quote.Ask.Value >= stopPrice.Value
                : quote.Quote.Bid.Value <= stopPrice.Value,
            TradeOccurred trade => side == Side.Buy
                ? trade.Trade.Price.Value >= stopPrice.Value
                : trade.Trade.Price.Value <= stopPrice.Value,
            _ => false
        };
    }

    protected static bool IsLimitTouched(Side side, Price limit, FinanceEvent evt, out Price fillPrice)
    {
        switch (evt)
        {
            case BarClosed bar:
                var touched = side == Side.Buy
                    ? bar.Bar.Low.Value <= limit.Value
                    : bar.Bar.High.Value >= limit.Value;
                fillPrice = limit;
                return touched;
            case QuoteReceived quote:
                var quoteTouched = side == Side.Buy
                    ? quote.Quote.Ask.Value <= limit.Value
                    : quote.Quote.Bid.Value >= limit.Value;
                fillPrice = limit;
                return quoteTouched;
            case TradeOccurred trade:
                var tradeTouched = side == Side.Buy
                    ? trade.Trade.Price.Value <= limit.Value
                    : trade.Trade.Price.Value >= limit.Value;
                fillPrice = limit;
                return tradeTouched;
            default:
                fillPrice = default;
                return false;
        }
    }

    protected bool IsStopLimitTouchedInBarOrder(Side side, Price stopPrice, Price limitPrice, Bar bar)
    {
        var stopTriggered = false;
        if (TouchesAfterStop(side, bar.Open, stopPrice, limitPrice, ref stopTriggered))
            return true;

        if (Config.BarOrdering == BarOrderingMode.Adaptive
            && Math.Abs(bar.Open.Value - bar.Low.Value) < Math.Abs(bar.Open.Value - bar.High.Value))
        {
            return TouchesAfterStop(side, bar.Low, stopPrice, limitPrice, ref stopTriggered)
                || TouchesAfterStop(side, bar.High, stopPrice, limitPrice, ref stopTriggered)
                || TouchesAfterStop(side, bar.Close, stopPrice, limitPrice, ref stopTriggered);
        }

        return TouchesAfterStop(side, bar.High, stopPrice, limitPrice, ref stopTriggered)
            || TouchesAfterStop(side, bar.Low, stopPrice, limitPrice, ref stopTriggered)
            || TouchesAfterStop(side, bar.Close, stopPrice, limitPrice, ref stopTriggered);
    }

    private static bool TouchesAfterStop(
        Side side,
        Price price,
        Price stopPrice,
        Price limitPrice,
        ref bool stopTriggered)
    {
        if (!stopTriggered)
        {
            stopTriggered = side == Side.Buy
                ? price.Value >= stopPrice.Value
                : price.Value <= stopPrice.Value;
        }

        return stopTriggered && (side == Side.Buy
            ? price.Value <= limitPrice.Value
            : price.Value >= limitPrice.Value);
    }

    protected void AddPending(in SimulatedOrder order, Price? limitPrice)
        => _pendingOrders.Add(new PendingOrder(
            order,
            limitPrice,
            RemainingQuantity: order.Intent.Quantity,
            StopTriggered: false,
            QueuePosition: Config.QueueModel.GetInitialPosition()));

    protected void FillPendingTouchedOrders(FinanceEvent evt)
    {
        if (Config.InitialMarketStatus != MarketStatus.Open)
            return;

        _completedOrderIndexes.Clear();
        for (var i = 0; i < _pendingOrders.Count; i++)
        {
            var pending = _pendingOrders[i];
            var order = pending.Order;
            if (!ShouldFillOnEvent(
                    in order,
                    pending.LimitPrice,
                    pending.StopTriggered,
                    evt,
                    out var fillPrice,
                    out var stopTriggered))
            {
                if (stopTriggered != pending.StopTriggered)
                    _pendingOrders[i] = pending with { StopTriggered = stopTriggered };
                continue;
            }

            var queuePosition = AdvanceQueuePosition(pending, evt);
            if (queuePosition > 0m)
            {
                _pendingOrders[i] = pending with
                {
                    QueuePosition = queuePosition,
                    StopTriggered = stopTriggered
                };
                continue;
            }

            var fillQuantity = DetermineFillQuantity(pending.RemainingQuantity, evt);
            Fill(in order, fillQuantity, fillPrice, isMaker: true);

            var remainingQuantity = pending.RemainingQuantity - fillQuantity;
            if (remainingQuantity.Value <= 0m)
            {
                _completedOrderIndexes.Add(i);
            }
            else
            {
                _pendingOrders[i] = pending with
                {
                    RemainingQuantity = remainingQuantity,
                    StopTriggered = stopTriggered,
                    QueuePosition = queuePosition
                };
            }
        }

        for (var i = _completedOrderIndexes.Count - 1; i >= 0; i--)
            _pendingOrders.RemoveAt(_completedOrderIndexes[i]);
    }

    public virtual void Dispose()
    {
        _executionEvents.Clear();
        _pendingOrders.Clear();
        _completedOrderIndexes.Clear();
    }

    protected readonly record struct SimulatedOrder(
        OrderId OrderId,
        OrderIntent Intent,
        Instrument Instrument,
        int VariantId);

    private Qty DetermineFillQuantity(Qty remainingQuantity, FinanceEvent evt)
    {
        if (Config.FillBehavior == FillBehavior.PartialFillOnTrade && evt is TradeOccurred trade)
        {
            return remainingQuantity.Value <= trade.Trade.Size.Value
                ? remainingQuantity
                : trade.Trade.Size;
        }

        return remainingQuantity;
    }

    private decimal AdvanceQueuePosition(in PendingOrder pending, FinanceEvent evt)
    {
        if (pending.QueuePosition <= 0m)
            return 0m;

        if (evt is not TradeOccurred trade || !TradeAdvancesQueue(pending, trade))
            return pending.QueuePosition;

        var advancement = Config.QueueModel.CalculateAdvancement(pending.QueuePosition, trade.Trade.Size.Value);
        return Math.Max(0m, pending.QueuePosition - advancement);
    }

    private static bool TradeAdvancesQueue(in PendingOrder pending, TradeOccurred trade)
    {
        var order = pending.Order;
        if (order.Instrument != trade.Instrument)
            return false;

        if (!pending.LimitPrice.HasValue)
            return true;

        var samePrice = trade.Trade.Price.Value == pending.LimitPrice.Value.Value;
        var oppositeAggressor = order.Intent.Side == Side.Buy
            ? trade.Trade.AggressorSide == Side.Sell
            : trade.Trade.AggressorSide == Side.Buy;
        return samePrice && oppositeAggressor;
    }

    protected readonly record struct PendingOrder(
        SimulatedOrder Order,
        Price? LimitPrice,
        Qty RemainingQuantity,
        bool StopTriggered,
        decimal QueuePosition);
}
