using HPD.Events;
using Rhodium.Connectivity.Simulation;
using Rhodium.Events;
using Rhodium.HFT;
using Rhodium.Primitives;

namespace Rhodium.Connectivity;

/// <summary>
/// Connector for backtesting - replays historical data with latency/queue simulation.
/// Implements the same IConnector interface as live connectors.
/// </summary>
public sealed class ReplayConnector : IConnector
{
    private readonly IAsyncEnumerable<FinanceEvent> _history;
    private readonly SimulationConfig _config;
    private readonly IFillModel _fillModel;
    private readonly IRiskGuard _riskGuard;
    private readonly Dictionary<OrderId, SimulatedOrder> _openOrders = [];
    private readonly Dictionary<Instrument, IHftDepth> _depths = [];

    private IEventCoordinator? _coordinator;
    private bool _isConnected;

    public ExchangeId Exchange => ExchangeId.Replay;
    public IRateLimiter RateLimiter => NoopRateLimiter.Instance;
    public bool IsConnected => _isConnected;

    public ReplayConnector(
        IAsyncEnumerable<FinanceEvent> history,
        SimulationConfig? config = null,
        IFillModel? fillModel = null,
        IRiskGuard? riskGuard = null)
    {
        _history = history;
        _config = config ?? SimulationConfig.Instant();
        _fillModel = fillModel ?? new DefaultFillModel();
        _riskGuard = riskGuard ?? new DefaultRiskGuard();
    }

    public async Task StartAsync(
        IEnumerable<Subscription> subscriptions,
        IEventCoordinator coordinator,
        CancellationToken ct)
    {
        _coordinator = coordinator;
        _isConnected = true;

        // Initialize depth tracking for subscribed instruments
        foreach (var sub in subscriptions)
        {
            if (sub.Type == SubscriptionType.Depth || sub.Type == SubscriptionType.Quotes)
            {
                // TODO: Get actual tick size and lot size from SecurityMetadata
                _depths[sub.Instrument] = new HashMapDepth(0.01m, 1m);
            }
        }

        try
        {
            await foreach (var evt in _history.WithCancellation(ct))
            {
                // 1. Update depth from market events
                UpdateDepth(evt);

                // 2. Emit market event to coordinator
                coordinator.Emit(evt);

                // 3. Check fills against this market event
                CheckFills(evt);
            }
        }
        finally
        {
            _isConnected = false;
        }
    }

    public Task SubmitOrderAsync(SubmitOrder command, CancellationToken ct)
    {
        if (_coordinator == null)
            throw new InvalidOperationException("Connector not started");

        // Risk check
        var depth = _depths.GetValueOrDefault(command.Instrument);
        var currentPrice = depth?.BestBidTick != null
            ? new Price(depth.BestBidTick.Value * depth.TickSize, Currency.USD)
            : (Price?)null;

        var riskCheck = _riskGuard.Check(command, currentPrice, 0m); // TODO: Get actual position
        if (!riskCheck.IsApproved)
        {
            _coordinator.Emit(new OrderRejected(
                command.OrderId,
                command.VariantId,
                riskCheck.Reason ?? "Risk check failed"));
            return Task.CompletedTask;
        }

        // Market orders fill immediately
        if (command.Type == OrderType.Market)
        {
            return SubmitMarketOrderAsync(command);
        }

        // Create simulated limit order
        var order = new SimulatedOrder
        {
            Command = command,
            SubmitTime = DateTimeOffset.UtcNow,
            QueuePosition = _config.Queue.GetInitialPosition()
        };

        _openOrders[command.OrderId] = order;

        // Emit accepted event
        _coordinator.Emit(new OrderAccepted(
            command.OrderId,
            command.VariantId));

        return Task.CompletedTask;
    }

    private Task SubmitMarketOrderAsync(SubmitOrder command)
    {
        if (_coordinator == null)
            throw new InvalidOperationException("Connector not started");

        var depth = _depths.GetValueOrDefault(command.Instrument);
        if (depth == null)
        {
            _coordinator.Emit(new OrderRejected(
                command.OrderId,
                command.VariantId,
                "No market data available"));
            return Task.CompletedTask;
        }

        // Determine fill price
        var fillPriceTick = command.Side == Side.Buy
            ? depth.BestAskTick
            : depth.BestBidTick;

        if (fillPriceTick == null)
        {
            _coordinator.Emit(new OrderRejected(
                command.OrderId,
                command.VariantId,
                "No liquidity available"));
            return Task.CompletedTask;
        }

        var fillPrice = new Price(fillPriceTick.Value * depth.TickSize, Currency.USD);

        // Apply slippage
        var slippageMoney = _config.Slippage.Calculate(fillPrice, command.Quantity, command.Side);
        fillPrice = new Price(fillPrice.Value + slippageMoney.Amount, fillPrice.Currency);

        // Calculate commission
        var commission = _config.Fees.Calculate(command.Quantity, fillPrice, isMaker: false);

        // Emit fill
        _coordinator.Emit(new OrderFilled(
            command.OrderId,
            command.Instrument,
            command.VariantId,
            command.Side,
            command.Quantity,
            fillPrice,
            commission));

        return Task.CompletedTask;
    }

    public Task CancelOrderAsync(CancelOrder command, CancellationToken ct)
    {
        if (_coordinator == null)
            throw new InvalidOperationException("Connector not started");

        if (_openOrders.TryGetValue(command.OrderId, out var order))
        {
            _openOrders.Remove(command.OrderId);
            _coordinator.Emit(new OrderCancelled(
                command.OrderId,
                order.Command.VariantId,
                order.Command.Quantity,
                "Cancelled by user"));
        }

        return Task.CompletedTask;
    }

    public Task ModifyOrderAsync(ModifyOrder command, CancellationToken ct)
    {
        if (_coordinator == null)
            throw new InvalidOperationException("Connector not started");

        if (_openOrders.TryGetValue(command.OrderId, out var order))
        {
            // Modify the order (simplified - real impl would handle price changes)
            if (command.NewQuantity.HasValue)
            {
                order.Command = order.Command with { Quantity = command.NewQuantity.Value };
            }
            if (command.NewLimitPrice.HasValue)
            {
                order.Command = order.Command with { LimitPrice = command.NewLimitPrice.Value };
                // Price change resets queue position
                order.QueuePosition = _config.Queue.GetInitialPosition();
            }

            // Note: OrderModified event doesn't exist yet, so emit OrderAccepted
            _coordinator.Emit(new OrderAccepted(
                command.OrderId,
                order.Command.VariantId));
        }

        return Task.CompletedTask;
    }

    private void UpdateDepth(FinanceEvent evt)
    {
        if (evt is QuoteReceived quote)
        {
            if (_depths.TryGetValue(quote.Instrument, out var depth) && depth is HashMapDepth hashDepth)
            {
                // Update depth with quote data via Update method
                var bidTick = (long)(quote.Quote.Bid.Value / depth.TickSize);
                var askTick = (long)(quote.Quote.Ask.Value / depth.TickSize);

                // HashMapDepth doesn't expose Update publicly, so we'll need to work with what we have
                // For now, we rely on the fact that BestBidTick/BestAskTick are updated through other means
                // This is a limitation of the current IHftDepth design
            }
        }
    }

    private void CheckFills(FinanceEvent evt)
    {
        if (_coordinator == null) return;

        // Check each open order for fills
        var filledOrders = new List<OrderId>();

        foreach (var (orderId, order) in _openOrders)
        {
            var depth = _depths.GetValueOrDefault(order.Command.Instrument);
            if (depth == null) continue;

            // Create fill context
            var limitPriceTick = order.Command.LimitPrice.HasValue
                ? (int)(order.Command.LimitPrice.Value.Value / depth.TickSize)
                : 0;

            var trade = evt is TradeOccurred tradeEvt ? (Trade?)tradeEvt.Trade : null;

            var ctx = new FillContext
            {
                OrderPriceTick = limitPriceTick,
                BestBidTick = depth.BestBidTick ?? 0,
                BestAskTick = depth.BestAskTick ?? 0,
                QueueRelativePosition = (double)order.QueuePosition,
                OrderQty = order.Command.Quantity,
                OrderSide = order.Command.Side,
                NominalFillPrice = order.Command.LimitPrice?.Value ?? 0m,
                Depth = depth,
                Trade = trade
            };

            // Check if order should fill
            if (_fillModel.ShouldFillLimit(ref ctx))
            {
                var fillPrice = _fillModel.AdjustFillPrice(ref ctx);
                var commission = _config.Fees.Calculate(order.Command.Quantity, fillPrice, isMaker: true);

                _coordinator.Emit(new OrderFilled(
                    orderId,
                    order.Command.Instrument,
                    order.Command.VariantId,
                    order.Command.Side,
                    order.Command.Quantity,
                    fillPrice,
                    commission));

                filledOrders.Add(orderId);
            }
            else
            {
                // Advance queue position based on market activity
                if (evt is TradeOccurred tradeEvent)
                {
                    order.QueuePosition = AdvanceQueuePosition(
                        order.QueuePosition,
                        tradeEvent,
                        order.Command.Side,
                        limitPriceTick);
                }
            }
        }

        // Remove filled orders
        foreach (var orderId in filledOrders)
        {
            _openOrders.Remove(orderId);
        }
    }

    private decimal AdvanceQueuePosition(
        decimal currentPosition,
        TradeOccurred trade,
        Side orderSide,
        int orderPriceTick)
    {
        var depth = _depths.GetValueOrDefault(trade.Instrument);
        if (depth == null) return currentPosition;

        // Simplified queue advancement
        // Real implementation would use QueueAdvancementKernel
        var tradePriceTick = (int)(trade.Trade.Price.Value / depth.TickSize);

        // Only advance if trade is at our price level
        if (tradePriceTick != orderPriceTick)
            return currentPosition;

        // Check if trade helps our position (aggressor on opposite side)
        bool tradeHelpsUs = (orderSide == Side.Buy && trade.Trade.AggressorSide == Side.Sell) ||
                           (orderSide == Side.Sell && trade.Trade.AggressorSide == Side.Buy);

        if (!tradeHelpsUs)
            return currentPosition;

        // Advance position based on queue model
        var advancement = _config.Queue.CalculateAdvancement(
            currentPosition,
            trade.Trade.Size.Value);

        return Math.Max(0m, currentPosition - advancement);
    }

    public void Dispose()
    {
        _openOrders.Clear();
        _depths.Clear();
        _isConnected = false;
    }

    private sealed class SimulatedOrder
    {
        public required SubmitOrder Command { get; set; }
        public required DateTimeOffset SubmitTime { get; init; }
        public decimal QueuePosition { get; set; }
    }
}
