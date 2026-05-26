using HPD.Events;
using Rhodium.Simulation;
using Rhodium.Events;
using Rhodium.HFT;
using Rhodium.Options;
using Rhodium.Primitives;
using System.Globalization;

namespace Rhodium.Connectivity;

/// <summary>
/// Legacy first-mover replay connector.
/// Kept as a behavioral oracle for <see cref="SimulationSession"/> parity tests and
/// certification tooling. New simulation and backtesting work should use
/// <see cref="SimulationSession"/> instead of connector-shaped replay.
/// </summary>
[Obsolete("ReplayConnector is a legacy behavioral oracle. Use Rhodium.Simulation.SimulationSession for simulation/backtesting.", error: false)]
public sealed class ReplayConnector : IConnector
{
    private const int MaxReplayBookLevels = 64;
    private const int MaxReplayTurnIterations = 1_000;

    private readonly IAsyncEnumerable<FinanceEvent> _history;
    private readonly SimulationConfig _config;
    private readonly IFillModel _fillModel;
    private readonly IRiskGuard _riskGuard;
    private readonly Money _initialCash;
    private readonly IInstrumentValuationModel _valuation = DefaultInstrumentValuationModel.Instance;
    private readonly Dictionary<OrderId, SimulatedOrder> _openOrders = [];
    private readonly ReplayOrderBook _restingBook = new();
    private readonly Dictionary<OrderId, StrategyId> _orderStrategyMap = [];
    private readonly Dictionary<OrderListId, ContingencyType> _orderListContingencies = [];
    private readonly Dictionary<OrderListId, OrderId> _otoParentOrders = [];
    private readonly Dictionary<OrderListId, OrderId> _ouoParentOrders = [];
    private readonly Dictionary<OrderListId, List<SubmitOrder>> _stagedOtoChildren = [];
    private readonly HashSet<OrderListId> _triggeredOtoLists = [];
    private readonly Dictionary<Venue, MarketStatus> _venueStatuses = [];
    private readonly Dictionary<Instrument, MarketStatus> _instrumentStatuses = [];
    private readonly Dictionary<Instrument, Price> _closingMarks = [];
    private readonly List<OrderId> _filledOrdersBuffer = [];
    private readonly Dictionary<(StrategyId StrategyId, Instrument Instrument, int VariantId), Position> _positions = [];
    private readonly Dictionary<(StrategyId StrategyId, Instrument Instrument, int VariantId), Qty> _settledPositions = [];
    private readonly Dictionary<(StrategyId StrategyId, int VariantId, Currency Currency), Money> _cashBalances = [];
    private readonly List<PendingSettlement> _pendingSettlements = [];
    private readonly List<PendingAssetDelivery> _pendingAssetDeliveries = [];
    private readonly SortedSet<InflightCommand> _inflightCommands = new(InflightCommandComparer.Instance);
    private readonly List<PendingResponseEvent> _pendingResponseEvents = [];
    private readonly List<ActiveAlgoOrder> _activeAlgoOrders = [];
    private readonly List<FinanceEvent> _pendingModuleEvents = [];
    private readonly List<AccountTradeNotional> _feeNotionalHistory = [];
    private readonly Dictionary<(StrategyId StrategyId, int VariantId, Currency Currency), ActiveMarginCall> _activeMarginCalls = [];
    private readonly Dictionary<OrderId, Qty> _orderFilledQuantities = [];
    private readonly Dictionary<Instrument, IHftDepth> _depths = [];
    private readonly HashSet<Instant> _processedModuleTimestamps = [];
    private long _nextVenueSequence;
    private long _nextInflightSequence;

    private IEventPublisher? _events;
    private ReplayModuleContext? _moduleContext;
    private Instant _currentReplayTime;
    private bool _isConnected;
    private bool _drainingModuleEvents;

    public IReadOnlyDictionary<Venue, ReplayVenueOrderPolicy> VenueOrderPolicies { get; set; } =
        new Dictionary<Venue, ReplayVenueOrderPolicy>();

    public IReadOnlyDictionary<Venue, ReplayVenueSimulationPolicy> VenueSimulationPolicies { get; set; } =
        new Dictionary<Venue, ReplayVenueSimulationPolicy>();

    public IReadOnlyDictionary<Instrument, InstrumentContract> InstrumentContracts { get; set; } =
        new Dictionary<Instrument, InstrumentContract>();

    public IReadOnlyList<IReplaySimulationModule> Modules { get; init; } = [];

    public ExchangeId Exchange => ExchangeId.Replay;
    public IRateLimiter RateLimiter => NoopRateLimiter.Instance;
    public bool IsConnected => _isConnected;

    internal Instant CurrentReplayTime => GetCashEventTime();

    public ReplayConnector(
        IAsyncEnumerable<FinanceEvent> history,
        SimulationConfig? config = null,
        IFillModel? fillModel = null,
        IRiskGuard? riskGuard = null,
        Money? initialCash = null)
    {
        _history = history;
        _config = config ?? SimulationConfig.Instant();
        _fillModel = fillModel ?? new DefaultFillModel();
        _riskGuard = riskGuard ?? new DefaultRiskGuard();
        _initialCash = initialCash ?? Money.USD(100_000m);
    }

    public async Task StartAsync(
        IEnumerable<Subscription> subscriptions,
        IEventPublisher events,
        CancellationToken ct)
    {
        _events = events;
        _moduleContext = new ReplayModuleContext(this);
        _isConnected = true;
        _processedModuleTimestamps.Clear();
        _pendingModuleEvents.Clear();
        foreach (var module in Modules)
            module.Reset();

        // Initialize depth tracking for subscribed instruments
        foreach (var sub in subscriptions)
        {
            if (sub.Type == SubscriptionType.Depth || sub.Type == SubscriptionType.Quotes)
            {
                // Replay defaults are used until venue metadata is attached to subscriptions.
                _depths[sub.Instrument] = new HashMapDepth(0.01m, 1m);
            }
        }

        try
        {
            await foreach (var evt in _history.WithCancellation(ct))
            {
                _currentReplayTime = GetEventTime(evt);

                // 1. Give modules first view of the replay input, then update replay market state.
                PreProcessModules(evt);
                UpdateMarketStatus(evt);
                UpdateDepth(evt);

                // 2. Release any cash proceeds that have settled by this event.
                ApplySettlements(_currentReplayTime);
                ProcessAssetDeliveries(_currentReplayTime);

                // 3. Settle deterministic same-time connector work before the market event is visible.
                DrainDueWork(_currentReplayTime);
                ProcessActiveAlgoOrders(_currentReplayTime, evt);

                // 4. Deliver any responses generated by algo slices.
                DrainDueWork(_currentReplayTime);

                // 5. Emit market event to the runtime event surface.
                events.Emit(evt);

                // 6. Check fills against this market event.
                CheckFills(evt);

                // 7. Settle any same-time work generated by fills, status changes, or strategy reactions.
                DrainDueWork(_currentReplayTime);

                // 8. Run timestamp modules once after the connector has settled this turn.
                ProcessModules(_currentReplayTime);
                DrainDueWork(_currentReplayTime);

                // 9. Mark margin positions after fills and current depth updates.
                EmitMarginStatusSnapshots();
            }
        }
        finally
        {
            CancelActiveAlgoOrders("Replay ended before algorithm completed.");
            EmitCustodyPositionSnapshots();
            EmitPendingSettlementStatuses();
            EmitAccountStatements();
            FlushPendingResponseEvents();
            foreach (var module in Modules)
                module.Reset();
            _isConnected = false;
            _moduleContext = null;
        }
    }

    public Task SubmitOrderAsync(SubmitOrder command, CancellationToken ct)
    {
        if (_events == null)
            throw new InvalidOperationException("Connector not started");

        if (_config.Latency.EntryMean > Duration.Zero)
        {
            EnqueueInflightSubmit(command, _config.Latency.EntryMean);
            return Task.CompletedTask;
        }

        return ProcessSubmitOrder(command);
    }

    private void PreProcessModules(FinanceEvent evt)
    {
        if (Modules.Count == 0 || _moduleContext is null)
            return;

        foreach (var module in Modules)
        {
            var sinks = new ReplayModuleSinks(_pendingModuleEvents);
            module.PreProcess(in evt, _moduleContext, sinks);
            DrainPendingModuleEvents();
        }
    }

    private void ProcessModules(Instant now)
    {
        if (Modules.Count == 0
            || _moduleContext is null
            || !_processedModuleTimestamps.Add(now))
        {
            return;
        }

        foreach (var module in Modules)
        {
            var sinks = new ReplayModuleSinks(_pendingModuleEvents);
            module.Process(now, _moduleContext, sinks);
            DrainPendingModuleEvents();
        }
    }

    private void DrainPendingModuleEvents()
    {
        if (_pendingModuleEvents.Count == 0 || _drainingModuleEvents)
            return;

        _drainingModuleEvents = true;
        try
        {
            while (_pendingModuleEvents.Count > 0)
            {
                var evt = _pendingModuleEvents[0];
                _pendingModuleEvents.RemoveAt(0);
                ProcessModuleGeneratedEvent(evt);
            }
        }
        finally
        {
            _drainingModuleEvents = false;
        }
    }

    private void ProcessModuleGeneratedEvent(FinanceEvent evt)
    {
        var previousTime = _currentReplayTime;
        var eventTime = GetEventTime(evt);
        if (eventTime != default)
            _currentReplayTime = eventTime;

        PreProcessModules(evt);
        UpdateMarketStatus(evt);
        UpdateDepth(evt);
        DrainDueWork(_currentReplayTime);
        _events?.Emit(evt);
        CheckFills(evt);
        DrainDueWork(_currentReplayTime);
        _currentReplayTime = previousTime;
    }

    public Task RequestAccountTransferAsync(AccountTransferCommand command, CancellationToken ct)
    {
        if (_events == null)
            throw new InvalidOperationException("Connector not started");

        EmitAccountTransferRequested(command);
        EmitAccountTransferStatus(command, AccountTransferStatus.Requested, reason: null);
        return Task.CompletedTask;
    }

    public Task CompleteAccountTransferAsync(AccountTransferCommand command, CancellationToken ct)
    {
        if (_events == null)
            throw new InvalidOperationException("Connector not started");

        if (!TryValidateAccountTransfer(command, out var reason))
        {
            EmitAccountTransferFailed(command, reason);
            return Task.CompletedTask;
        }

        if (!TryApplyCompletedAccountTransfer(command, out reason))
        {
            EmitAccountTransferFailed(command, reason);
            return Task.CompletedTask;
        }

        var now = GetCashEventTime();
        EmitConnectorEvent(new AccountTransferCompleted(
            command.TransferId,
            command.StrategyId,
            command.VariantId,
            command.TransferType,
            command.CashAmount,
            command.Instrument,
            command.Quantity,
            now,
            command.ExternalReference,
            command.DestinationStrategyId,
            command.DestinationVariantId,
            Venue: command.Instrument?.Venue,
            CarryingPrice: command.CarryingPrice)
        {
            Time = now
        });
        EmitAccountTransferStatus(command, AccountTransferStatus.Completed, reason: null);
        return Task.CompletedTask;
    }

    public Task CancelAccountTransferAsync(AccountTransferCommand command, string? reason, CancellationToken ct)
    {
        if (_events == null)
            throw new InvalidOperationException("Connector not started");

        var now = GetCashEventTime();
        EmitConnectorEvent(new AccountTransferCanceled(
            command.TransferId,
            command.StrategyId,
            command.VariantId,
            command.TransferType,
            command.CashAmount,
            command.Instrument,
            command.Quantity,
            now,
            reason,
            command.ExternalReference,
            command.DestinationStrategyId,
            command.DestinationVariantId,
            Venue: command.Instrument?.Venue,
            CarryingPrice: command.CarryingPrice)
        {
            Time = now
        });
        EmitAccountTransferStatus(command, AccountTransferStatus.Canceled, reason);
        return Task.CompletedTask;
    }

    public Task FailAccountTransferAsync(AccountTransferCommand command, string reason, CancellationToken ct)
    {
        if (_events == null)
            throw new InvalidOperationException("Connector not started");

        EmitAccountTransferFailed(command, reason);
        return Task.CompletedTask;
    }

    public Task ApplyCorporateActionAsync(CorporateActionCommand command, CancellationToken ct)
    {
        if (_events == null)
            throw new InvalidOperationException("Connector not started");

        ct.ThrowIfCancellationRequested();
        var effectiveAt = command.EffectiveAt == default ? GetCashEventTime() : command.EffectiveAt;
        EmitConnectorEvent(new CorporateActionApplied(
            command.CorporateActionId,
            command.ActionType,
            command.Instrument,
            effectiveAt,
            command.SplitRatio,
            command.DividendPerShare,
            command.ExternalReference)
        {
            Time = effectiveAt
        });

        switch (command.ActionType)
        {
            case CorporateActionType.StockSplit:
                ApplyStockSplit(command, effectiveAt);
                break;

            case CorporateActionType.CashDividend:
                ApplyCashDividend(command, effectiveAt);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(command), $"Corporate action type {command.ActionType} is not supported.");
        }

        return Task.CompletedTask;
    }

    public Task ApplyFinancingChargeAsync(FinancingChargeCommand command, CancellationToken ct)
    {
        if (_events == null)
            throw new InvalidOperationException("Connector not started");

        ct.ThrowIfCancellationRequested();
        if (command.Amount.Amount == 0m)
            throw new ArgumentOutOfRangeException(nameof(command), "Financing charge amount cannot be zero.");

        var effectiveAt = command.EffectiveAt == default ? GetCashEventTime() : command.EffectiveAt;
        var cash = GetCashBalance(command.StrategyId, command.VariantId, command.Amount.Currency);
        SetCashBalance(command.StrategyId, command.VariantId, cash + command.Amount);

        EmitConnectorEvent(new FinancingChargeApplied(
            command.FinancingChargeId,
            command.ChargeType,
            command.StrategyId,
            command.VariantId,
            command.Amount,
            effectiveAt,
            command.Instrument,
            command.Quantity,
            command.Rate,
            command.ExternalReference)
        {
            Time = effectiveAt
        });
        EmitPerformanceSnapshot(command.StrategyId, command.VariantId, command.Amount.Currency);
        EmitAccountStatement(command.StrategyId, command.VariantId, command.Amount.Currency);
        return Task.CompletedTask;
    }

    private Task ProcessSubmitOrder(SubmitOrder command)
    {
        if (_events == null)
            throw new InvalidOperationException("Connector not started");

        var marketStatus = GetEffectiveMarketStatus(command.Instrument);
        if (marketStatus != MarketStatus.Open)
        {
            EmitConnectorEvent(new OrderRejected(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                $"Market is {marketStatus}; replay order submission is disabled."));
            return Task.CompletedTask;
        }

        if (!ValidateVenueSimulationPolicy(command))
            return Task.CompletedTask;

        if (TryStageOtoChild(command))
            return Task.CompletedTask;

        if (!ValidateVenueOrderPolicy(command))
            return Task.CompletedTask;

        if (!ValidateDisplayQuantity(command))
            return Task.CompletedTask;

        if (!string.IsNullOrWhiteSpace(command.ExecAlgorithmId))
            return StartAlgorithmicOrder(command);

        // Risk check
        var depth = _depths.GetValueOrDefault(command.Instrument);
        var currentPrice = depth?.BestBidTick != null
            ? new Price(depth.BestBidTick.Value * depth.TickSize, Currency.USD)
            : (Price?)null;

        var riskCheck = _riskGuard.Check(command, currentPrice, GetPositionQuantity(command));
        if (!riskCheck.IsApproved)
        {
            EmitConnectorEvent(new OrderRejected(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                riskCheck.Reason ?? "Risk check failed"));
            return Task.CompletedTask;
        }

        // Market orders fill immediately
        if (command.Type == OrderType.Market)
        {
            if (command.PostOnly)
            {
                EmitConnectorEvent(new OrderRejected(
                    command.OrderId,
                    command.StrategyId,
                    command.VariantId,
                    "PostOnly market orders are invalid because they would take liquidity."));
                return Task.CompletedTask;
            }

            if (GetVenueSimulationPolicy(command.Instrument.Venue).UseMarketOrderAcks)
            {
                EmitConnectorEvent(new OrderAccepted(
                    command.OrderId,
                    command.StrategyId,
                    command.VariantId));
            }

            return CheckAccountConstraints(command)
                ? SubmitMarketOrderAsync(command)
                : Task.CompletedTask;
        }

        if (!IsReplaySupportedOrder(command.Type))
        {
            EmitConnectorEvent(new OrderRejected(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                $"ReplayConnector does not support {command.Type} orders yet."));
            return Task.CompletedTask;
        }

        if (command.Type is OrderType.TrailingStopMarket or OrderType.TrailingStopLimit
            && (!command.TrailingOffset.HasValue || !command.TrailingOffsetType.HasValue))
        {
            EmitConnectorEvent(new OrderRejected(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                $"{command.Type} requires trailing offset and offset type."));
            return Task.CompletedTask;
        }

        if (command.TimeInForce == TimeInForce.GTD && !command.GoodTilDate.HasValue)
        {
            EmitConnectorEvent(new OrderRejected(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                "GTD orders require GoodTilDate."));
            return Task.CompletedTask;
        }

        if (command.PostOnly && WouldTakeLiquidity(command))
        {
            EmitConnectorEvent(new OrderRejected(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                "PostOnly order would take liquidity."));
            return Task.CompletedTask;
        }

        if (!CheckAccountConstraints(command))
            return Task.CompletedTask;

        if (!TryRegisterOrderList(command))
            return Task.CompletedTask;

        // Create replay order state for resting or trigger-driven orders.
        var order = new SimulatedOrder
        {
            Command = command,
            RemainingQuantity = command.Quantity,
            ReservedCash = EstimateCapitalRequirement(command),
            SubmitTime = GetCashEventTime(),
            QueuePosition = _config.QueueModel.GetInitialPosition(),
            DisplayRemaining = command.DisplayQuantity,
            VenueSequence = NextVenueSequence()
        };

        EmitConnectorEvent(new OrderAccepted(
            command.OrderId,
            command.StrategyId,
            command.VariantId));

        if (command.TimeInForce == TimeInForce.FOK
            && _restingBook.WouldCross(command, _openOrders)
            && _restingBook.GetCrossingAvailableQuantity(command, _openOrders) < command.Quantity.Value)
        {
            EmitConnectorEvent(new OrderCancelled(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                command.Quantity,
                "FOK order was not fully fillable against replay resting liquidity."));
            return Task.CompletedTask;
        }

        MatchCrossedRestingOrders(command.OrderId, order);
        if (order.RemainingQuantity.Value <= 0m)
            return Task.CompletedTask;

        if (command.TimeInForce is TimeInForce.IOC or TimeInForce.FOK)
        {
            if (TryFillImmediately(order, out var immediateFillPrice))
                EmitFill(command.OrderId, order, immediateFillPrice, order.RemainingQuantity, isMaker: true);
            else
                EmitConnectorEvent(new OrderCancelled(
                    command.OrderId,
                    command.StrategyId,
                    command.VariantId,
                    order.RemainingQuantity,
                    $"{command.TimeInForce} order was not immediately fillable."));

            return Task.CompletedTask;
        }

        AddOpenOrder(command.OrderId, order);

        return Task.CompletedTask;
    }

    private bool ValidateVenueOrderPolicy(SubmitOrder command)
    {
        var policy = GetVenueOrderPolicy(command.Instrument.Venue);

        if (policy.AllowedOrderTypes is not null && !policy.AllowedOrderTypes.Contains(command.Type))
        {
            EmitConnectorEvent(new OrderRejected(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                $"{command.Instrument.Venue} replay policy does not allow {command.Type} orders."));
            return false;
        }

        if (policy.AllowedTimeInForce is not null && !policy.AllowedTimeInForce.Contains(command.TimeInForce))
        {
            EmitConnectorEvent(new OrderRejected(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                $"{command.Instrument.Venue} replay policy does not allow {command.TimeInForce} orders."));
            return false;
        }

        if (!policy.AllowPostOnly && command.PostOnly)
        {
            EmitConnectorEvent(new OrderRejected(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                $"{command.Instrument.Venue} replay policy does not allow post-only orders."));
            return false;
        }

        if (policy.MinOrderQuantity is { } minimumQuantity && command.Quantity < minimumQuantity)
        {
            EmitConnectorEvent(new OrderRejected(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                $"{command.Instrument.Venue} replay policy requires minimum order quantity {minimumQuantity}."));
            return false;
        }

        if (policy.MinOrderNotional is { } minimumNotional
            && !MeetsVenueMinimumNotional(command, minimumNotional, out var reason))
        {
            EmitConnectorEvent(new OrderRejected(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                reason));
            return false;
        }

        return true;
    }

    private bool ValidateVenueSimulationPolicy(SubmitOrder command)
    {
        var policy = GetVenueSimulationPolicy(command.Instrument.Venue);

        if (policy.FrozenAccount)
        {
            EmitConnectorEvent(new OrderRejected(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                $"{command.Instrument.Venue} replay account is frozen."));
            return false;
        }

        if (!policy.SupportContingentOrders
            && (command.OrderListId.HasValue || command.ContingencyType.HasValue))
        {
            EmitConnectorEvent(new OrderRejected(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                $"{command.Instrument.Venue} replay policy does not support contingent orders."));
            return false;
        }

        if (policy.RejectTriggeredOrdersInMarket
            && IsTriggeredOrder(command.Type)
            && IsOpenForTrading(command.Instrument))
        {
            EmitConnectorEvent(new OrderRejected(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                $"{command.Instrument.Venue} replay policy rejects triggered orders while the market is open."));
            return false;
        }

        return true;
    }

    private bool MeetsVenueMinimumNotional(SubmitOrder command, Money minimumNotional, out string reason)
    {
        reason = string.Empty;
        if (minimumNotional.Amount <= 0m)
            return true;

        if (!TryResolveOrderPolicyReferencePrice(command, out var price))
        {
            reason = $"{command.Instrument.Venue} replay policy requires a reference price for minimum notional checks.";
            return false;
        }

        var contract = GetContract(command.Instrument);
        var notional = _valuation.Notional(contract, command.Quantity, price);
        if (notional.Currency != minimumNotional.Currency)
        {
            reason = $"{command.Instrument.Venue} replay policy minimum notional currency {minimumNotional.Currency} does not match order notional currency {notional.Currency}.";
            return false;
        }

        if (notional.Amount >= minimumNotional.Amount)
            return true;

        reason = $"{command.Instrument.Venue} replay policy requires minimum order notional {minimumNotional}.";
        return false;
    }

    private bool TryResolveOrderPolicyReferencePrice(SubmitOrder command, out Price price)
    {
        if (command.LimitPrice.HasValue)
        {
            price = command.LimitPrice.Value;
            return true;
        }

        if (command.StopPrice.HasValue)
        {
            price = command.StopPrice.Value;
            return true;
        }

        var depth = _depths.GetValueOrDefault(command.Instrument);
        var tick = command.Side == Side.Buy
            ? depth?.BestAskTick
            : depth?.BestBidTick;

        if (depth is not null && tick.HasValue)
        {
            price = new Price(tick.Value * depth.TickSize, Currency.USD);
            return true;
        }

        price = default;
        return false;
    }

    private MarketStatus GetEffectiveMarketStatus(Instrument instrument)
    {
        if (_instrumentStatuses.TryGetValue(instrument, out var instrumentStatus))
            return instrumentStatus;

        if (_venueStatuses.TryGetValue(instrument.Venue, out var venueStatus))
            return venueStatus;

        return _config.InitialMarketStatus;
    }

    private bool IsOpenForTrading(Instrument instrument)
        => GetEffectiveMarketStatus(instrument) == MarketStatus.Open;

    internal MarketStatus GetEffectiveMarketStatusForModule(Instrument instrument)
        => GetEffectiveMarketStatus(instrument);

    internal IHftDepth? GetDepthForModule(Instrument instrument)
        => _depths.GetValueOrDefault(instrument);

    private ReplayVenueOrderPolicy GetVenueOrderPolicy(Venue venue)
        => VenueOrderPolicies.TryGetValue(venue, out var policy)
            ? policy
            : ReplayVenueOrderPolicy.Default;

    private ReplayVenueSimulationPolicy GetVenueSimulationPolicy(Venue venue)
        => VenueSimulationPolicies.TryGetValue(venue, out var policy)
            ? policy
            : ReplayVenueSimulationPolicy.Default;

    private void AddOpenOrder(OrderId orderId, SimulatedOrder order)
    {
        _openOrders[orderId] = order;
        _orderStrategyMap[orderId] = order.Command.StrategyId;
        _restingBook.AddOrUpdate(orderId, order, losesPriority: true);
    }

    private void RemoveOpenOrder(OrderId orderId)
    {
        _openOrders.Remove(orderId);
        _orderStrategyMap.Remove(orderId);
        _restingBook.Remove(orderId);
    }

    private bool TryRegisterOrderList(SubmitOrder command)
    {
        if (!command.OrderListId.HasValue)
            return true;

        var contingency = command.ContingencyType ?? ContingencyType.OCO;
        if (_orderListContingencies.TryGetValue(command.OrderListId.Value, out var existing)
            && existing != contingency)
        {
            EmitConnectorEvent(new OrderRejected(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                $"Order list {command.OrderListId.Value} already uses {existing}; cannot add {contingency}."));
            return false;
        }

        _orderListContingencies[command.OrderListId.Value] = contingency;
        if (contingency == ContingencyType.OTO
            && !_otoParentOrders.ContainsKey(command.OrderListId.Value)
            && !_triggeredOtoLists.Contains(command.OrderListId.Value))
        {
            _otoParentOrders[command.OrderListId.Value] = command.OrderId;
        }
        else if (contingency == ContingencyType.OUO
            && !_ouoParentOrders.ContainsKey(command.OrderListId.Value))
        {
            _ouoParentOrders[command.OrderListId.Value] = command.OrderId;
        }

        return true;
    }

    private bool TryStageOtoChild(SubmitOrder command)
    {
        if (!command.OrderListId.HasValue || (command.ContingencyType ?? ContingencyType.OCO) != ContingencyType.OTO)
            return false;

        var orderListId = command.OrderListId.Value;
        if (!_orderListContingencies.TryGetValue(orderListId, out var existing))
        {
            _orderListContingencies[orderListId] = ContingencyType.OTO;
            _otoParentOrders[orderListId] = command.OrderId;
            return false;
        }

        if (existing != ContingencyType.OTO)
            return false;

        if (_triggeredOtoLists.Contains(orderListId))
            return false;

        if (!_otoParentOrders.TryGetValue(orderListId, out var parentOrderId))
        {
            _otoParentOrders[orderListId] = command.OrderId;
            return false;
        }

        if (parentOrderId == command.OrderId)
            return false;

        if (!_stagedOtoChildren.TryGetValue(orderListId, out var children))
        {
            children = [];
            _stagedOtoChildren[orderListId] = children;
        }

        children.Add(command);
        return true;
    }

    private void EnqueueInflightSubmit(SubmitOrder command, Duration delay)
        => _inflightCommands.Add(InflightCommand.Submit(
            command,
            GetCashEventTime() + delay,
            NextInflightSequence()));

    private void EnqueueInflightModify(ModifyOrder command, Duration delay)
        => _inflightCommands.Add(InflightCommand.Modify(
            command,
            GetCashEventTime() + delay,
            NextInflightSequence()));

    private void EnqueueInflightCancel(CancelOrder command, Duration delay)
        => _inflightCommands.Add(InflightCommand.Cancel(
            command,
            GetCashEventTime() + delay,
            NextInflightSequence()));

    private long NextInflightSequence() => ++_nextInflightSequence;

    private void DrainDueWork(Instant now)
    {
        for (var iteration = 0; iteration < MaxReplayTurnIterations; iteration++)
        {
            var processed = false;
            processed |= ProcessInflightCommands(now);
            processed |= ProcessPendingResponseEvents(now);

            if (!processed)
                return;
        }

        throw new InvalidOperationException(
            $"Replay turn at {now} exceeded {MaxReplayTurnIterations} iterations while draining same-time work.");
    }

    private bool ProcessInflightCommands(Instant now)
    {
        var processed = false;
        while (_inflightCommands.Count > 0)
        {
            var pending = _inflightCommands.Min;
            if (pending.ArrivesAt > now)
                return processed;

            _inflightCommands.Remove(pending);
            processed = true;
            switch (pending.Kind)
            {
                case InflightCommandKind.Cancel:
                    ProcessCancelOrder(pending.CancelCommand!.Value);
                    break;
                case InflightCommandKind.Modify:
                    ProcessModifyOrder(pending.ModifyCommand!.Value);
                    break;
                case InflightCommandKind.Submit:
                    ProcessSubmitOrder(pending.SubmitCommand!.Value);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(pending), $"Unsupported inflight command kind {pending.Kind}.");
            }
        }

        return processed;
    }

    private Task StartAlgorithmicOrder(SubmitOrder command)
    {
        var algo = command.ExecAlgorithmId?.Trim().ToUpperInvariant();
        if (algo is not ("TWAP" or "VWAP" or "POV"))
        {
            EmitConnectorEvent(new OrderRejected(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                $"ReplayConnector does not support execution algorithm {command.ExecAlgorithmId}."));
            return Task.CompletedTask;
        }

        var hasHorizon = TryGetAlgoHorizon(command, out var horizon);
        if (!hasHorizon && algo != "POV")
        {
            EmitConnectorEvent(new OrderRejected(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                $"{algo} requires a positive horizon_secs parameter."));
            return Task.CompletedTask;
        }

        if (algo == "POV" && !TryGetParticipationRate(command, out _))
        {
            EmitConnectorEvent(new OrderRejected(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                "POV requires a positive participation_rate parameter."));
            return Task.CompletedTask;
        }

        var interval = TryGetAlgoInterval(command, out var parsedInterval)
            ? parsedInterval
            : algo is "VWAP" or "POV"
                ? Duration.Zero
                : horizon;

        if (algo == "TWAP" && interval <= Duration.Zero)
            interval = horizon;

        EmitConnectorEvent(new OrderAccepted(
            command.OrderId,
            command.StrategyId,
            command.VariantId));

        _activeAlgoOrders.Add(new ActiveAlgoOrder
        {
            Command = command,
            AlgorithmId = algo,
            RemainingQuantity = command.Quantity,
            StartedAt = GetCashEventTime(),
            EndsAt = hasHorizon ? GetCashEventTime() + horizon : Instant.MaxValue,
            Interval = interval,
            NextSliceAt = interval > Duration.Zero ? GetCashEventTime() + interval : GetCashEventTime(),
            ParticipationRate = TryGetParticipationRate(command, out var rate) ? rate : 1m,
            ForceCompleteAtHorizon = algo is "TWAP" or "VWAP"
        });
        return Task.CompletedTask;
    }

    private void ProcessActiveAlgoOrders(Instant now, FinanceEvent evt)
    {
        for (var i = _activeAlgoOrders.Count - 1; i >= 0; i--)
        {
            var algo = _activeAlgoOrders[i];
            if (algo.RemainingQuantity.Value <= 0m)
            {
                _activeAlgoOrders.RemoveAt(i);
                continue;
            }

            if (algo.AlgorithmId == "TWAP")
                ProcessTwapSlice(algo, now);
            else if (algo.AlgorithmId == "VWAP")
                ProcessVwapSlice(algo, now, evt);
            else if (algo.AlgorithmId == "POV")
                ProcessPovSlice(algo, now, evt);

            if (algo.RemainingQuantity.Value <= 0m || now >= algo.EndsAt)
            {
                if (algo.RemainingQuantity.Value > 0m)
                {
                    if (algo.ForceCompleteAtHorizon)
                        SubmitAlgoMarketSlice(algo, algo.RemainingQuantity);
                    else
                        CancelActiveAlgoOrder(algo, "Algorithm horizon ended before completion.");
                }

                _activeAlgoOrders.RemoveAt(i);
            }
        }
    }

    private void ProcessTwapSlice(ActiveAlgoOrder algo, Instant now)
    {
        while (algo.RemainingQuantity.Value > 0m && algo.NextSliceAt <= now)
        {
            var remainingIntervals = Math.Max(1, (int)Math.Ceiling((algo.EndsAt - algo.NextSliceAt).TotalSeconds / Math.Max(1e-9, algo.Interval.TotalSeconds)) + 1);
            var sliceQty = new Qty(Math.Min(
                algo.RemainingQuantity.Value,
                Math.Ceiling(algo.RemainingQuantity.Value / remainingIntervals * 1_000_000m) / 1_000_000m));
            SubmitAlgoMarketSlice(algo, sliceQty);
            algo.NextSliceAt += algo.Interval;
        }
    }

    private void ProcessVwapSlice(ActiveAlgoOrder algo, Instant now, FinanceEvent evt)
    {
        if (algo.NextSliceAt > now)
            return;

        var eventVolume = GetEventVolume(evt);
        if (eventVolume <= 0m && now < algo.EndsAt)
            return;

        var targetQty = eventVolume > 0m
            ? new Qty(Math.Min(algo.RemainingQuantity.Value, eventVolume * algo.ParticipationRate))
            : algo.RemainingQuantity;
        if (targetQty.Value <= 0m)
            return;

        SubmitAlgoMarketSlice(algo, targetQty);
        algo.NextSliceAt = now + algo.Interval;
    }

    private void ProcessPovSlice(ActiveAlgoOrder algo, Instant now, FinanceEvent evt)
    {
        if (algo.NextSliceAt > now)
            return;

        var eventVolume = GetEventVolume(evt);
        if (eventVolume <= 0m)
            return;

        var targetQty = new Qty(Math.Min(algo.RemainingQuantity.Value, eventVolume * algo.ParticipationRate));
        if (targetQty.Value <= 0m)
            return;

        SubmitAlgoMarketSlice(algo, targetQty);
        if (algo.Interval > Duration.Zero)
            algo.NextSliceAt = now + algo.Interval;
    }

    private void SubmitAlgoMarketSlice(ActiveAlgoOrder algo, Qty quantity)
    {
        if (quantity.Value <= 0m)
            return;

        var slice = algo.Command with
        {
            Quantity = quantity,
            ExecAlgorithmId = null,
            ExecAlgorithmParams = null,
            OrderListId = null,
            ContingencyType = null,
            DisplayQuantity = null
        };

        ProcessSubmitOrder(slice);
        algo.RemainingQuantity = new Qty(Math.Max(0m, algo.RemainingQuantity.Value - quantity.Value));
    }

    private void CancelActiveAlgoOrder(ActiveAlgoOrder algo, string reason)
    {
        if (algo.RemainingQuantity.Value <= 0m)
            return;

        EmitConnectorEvent(new OrderCancelled(
            algo.Command.OrderId,
            algo.Command.StrategyId,
            algo.Command.VariantId,
            algo.RemainingQuantity,
            reason));
        algo.RemainingQuantity = new Qty(0m);
    }

    private void CancelActiveAlgoOrders(string reason)
    {
        foreach (var algo in _activeAlgoOrders)
            CancelActiveAlgoOrder(algo, reason);

        _activeAlgoOrders.Clear();
    }

    private static decimal GetEventVolume(FinanceEvent evt)
        => evt switch
        {
            TradeOccurred trade => trade.Trade.Size.Value,
            BarClosed bar => bar.Bar.Volume.Value,
            QuoteReceived quote => quote.Quote.BidSize.Value + quote.Quote.AskSize.Value,
            BookSnapshotReceived book => book.Book.Bids.Sum(static level => level.Size.Value)
                + book.Book.Asks.Sum(static level => level.Size.Value),
            BookLevelDeltaReceived delta => delta.Delta.Size.Value,
            BookLevelDeltasReceived deltas => deltas.Deltas.Sum(static delta => delta.Size.Value),
            BookDepthSnapshotReceived depth => depth.Bids.Sum(static level => level.Size.Value)
                + depth.Asks.Sum(static level => level.Size.Value),
            BookDepth10Received depth => depth.Bids.Take(10).Sum(static level => level.Size.Value)
                + depth.Asks.Take(10).Sum(static level => level.Size.Value),
            _ => 0m
        };

    private static bool TryGetAlgoHorizon(SubmitOrder command, out Duration horizon)
    {
        horizon = default;
        if (command.ExecAlgorithmParams == null
            || !command.ExecAlgorithmParams.TryGetValue("horizon_secs", out var raw)
            || !long.TryParse(raw, out var seconds)
            || seconds <= 0)
        {
            return false;
        }

        horizon = Duration.FromSeconds(seconds);
        return true;
    }

    private static bool TryGetAlgoInterval(SubmitOrder command, out Duration interval)
    {
        interval = default;
        if (command.ExecAlgorithmParams == null
            || !command.ExecAlgorithmParams.TryGetValue("interval_secs", out var raw)
            || !long.TryParse(raw, out var seconds)
            || seconds <= 0)
        {
            return false;
        }

        interval = Duration.FromSeconds(seconds);
        return true;
    }

    private static bool TryGetParticipationRate(SubmitOrder command, out decimal rate)
    {
        rate = default;
        if (command.ExecAlgorithmParams == null
            || !command.ExecAlgorithmParams.TryGetValue("participation_rate", out var raw)
            || !decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out rate)
            || rate <= 0m)
        {
            return false;
        }

        return true;
    }

    private void EmitConnectorEvent(FinanceEvent evt)
    {
        EmitConnectorEventCore(evt);
        if (evt is not OrderFilled && TryCreateOrderStateSnapshot(evt, out var snapshot))
        {
            EmitConnectorEventCore(snapshot);
            TrackOrderStateSnapshot(snapshot);
        }
    }

    private void EmitConnectorEventCore(FinanceEvent evt)
    {
        if (_events == null)
            return;

        var exchangeTime = GetCashEventTime();
        if (_config.Latency.ResponseMean <= Duration.Zero)
        {
            _events.Emit(evt with { Time = exchangeTime });
            return;
        }

        var visibleAt = exchangeTime + _config.Latency.ResponseMean;
        _pendingResponseEvents.Add(new PendingResponseEvent(
            evt with { Time = visibleAt },
            visibleAt));
    }

    private static bool TryCreateOrderStateSnapshot(FinanceEvent evt, out OrderStateSnapshot snapshot)
    {
        snapshot = evt switch
        {
            OrderAccepted accepted => new OrderStateSnapshot(
                accepted.OrderId,
                accepted.StrategyId,
                accepted.VariantId,
                OrderStatus.Open),
            OrderModified modified => new OrderStateSnapshot(
                modified.OrderId,
                modified.StrategyId,
                modified.VariantId,
                OrderStatus.Open,
                RemainingQty: modified.NewQuantity),
            OrderRejected rejected => new OrderStateSnapshot(
                rejected.OrderId,
                rejected.StrategyId,
                rejected.VariantId,
                OrderStatus.Rejected,
                Reason: rejected.Reason),
            OrderCancelled cancelled => new OrderStateSnapshot(
                cancelled.OrderId,
                cancelled.StrategyId,
                cancelled.VariantId,
                OrderStatus.Cancelled,
                RemainingQty: cancelled.RemainingQty,
                Reason: cancelled.Reason),
            OrderExpired expired => new OrderStateSnapshot(
                expired.OrderId,
                expired.StrategyId,
                expired.VariantId,
                OrderStatus.Expired),
            _ => null!
        };

        return snapshot is not null;
    }

    private void TrackOrderStateSnapshot(OrderStateSnapshot snapshot)
    {
        switch (snapshot.Status)
        {
            case OrderStatus.Open when snapshot.FilledQty is null:
                _orderFilledQuantities[snapshot.OrderId] = Qty.Zero;
                break;
            case OrderStatus.Cancelled:
            case OrderStatus.Rejected:
            case OrderStatus.Expired:
                _orderFilledQuantities.Remove(snapshot.OrderId);
                break;
        }
    }

    private void EmitOrderFillState(
        OrderId orderId,
        StrategyId strategyId,
        int variantId,
        Qty fillQuantity,
        Qty remainingQuantity)
    {
        var previousFilled = _orderFilledQuantities.GetValueOrDefault(orderId, Qty.Zero);
        var cumulativeFilled = previousFilled + fillQuantity;
        var status = remainingQuantity.Value <= 0m
            ? OrderStatus.Filled
            : OrderStatus.PartiallyFilled;

        EmitConnectorEventCore(new OrderStateSnapshot(
            orderId,
            strategyId,
            variantId,
            status,
            FilledQty: cumulativeFilled,
            RemainingQty: remainingQuantity));

        if (status == OrderStatus.Filled)
            _orderFilledQuantities.Remove(orderId);
        else
            _orderFilledQuantities[orderId] = cumulativeFilled;
    }

    private bool ProcessPendingResponseEvents(Instant now)
    {
        if (_events == null)
            return false;

        var processed = false;
        for (var i = 0; i < _pendingResponseEvents.Count; i++)
        {
            var pending = _pendingResponseEvents[i];
            if (pending.VisibleAt > now)
                continue;

            _events.Emit(pending.Event);
            _pendingResponseEvents.RemoveAt(i);
            i--;
            processed = true;
        }

        return processed;
    }

    private void FlushPendingResponseEvents()
    {
        if (_events == null)
            return;

        foreach (var pending in _pendingResponseEvents)
            _events.Emit(pending.Event);

        _pendingResponseEvents.Clear();
    }

    private static bool IsReplaySupportedOrder(OrderType type)
        => type is OrderType.Limit
            or OrderType.StopMarket
            or OrderType.StopLimit
            or OrderType.MarketIfTouched
            or OrderType.LimitIfTouched
            or OrderType.MarketToLimit
            or OrderType.TrailingStopMarket
            or OrderType.TrailingStopLimit;

    private bool ValidateDisplayQuantity(SubmitOrder command)
    {
        if (!command.DisplayQuantity.HasValue)
            return true;

        var displayQuantity = command.DisplayQuantity.Value;
        if (displayQuantity.Value <= 0m)
        {
            EmitConnectorEvent(new OrderRejected(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                "Iceberg display quantity must be positive."));
            return false;
        }

        if (displayQuantity.Value >= command.Quantity.Value)
        {
            EmitConnectorEvent(new OrderRejected(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                "Iceberg display quantity must be smaller than total order quantity."));
            return false;
        }

        if (!IsDisplaySupportedOrder(command.Type) || !command.LimitPrice.HasValue)
        {
            EmitConnectorEvent(new OrderRejected(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                "Iceberg display quantity is supported only for limit-style resting orders."));
            return false;
        }

        return true;
    }

    private static bool IsDisplaySupportedOrder(OrderType type)
        => type is OrderType.Limit
            or OrderType.StopLimit
            or OrderType.LimitIfTouched
            or OrderType.TrailingStopLimit;

    private static bool IsTriggeredOrder(OrderType type)
        => type is OrderType.StopMarket
            or OrderType.StopLimit
            or OrderType.MarketIfTouched
            or OrderType.LimitIfTouched
            or OrderType.TrailingStopMarket
            or OrderType.TrailingStopLimit;

    private bool WouldTakeLiquidity(SubmitOrder command)
    {
        if (!command.LimitPrice.HasValue)
            return true;

        if (_restingBook.WouldCross(command, _openOrders))
            return true;

        var depth = _depths.GetValueOrDefault(command.Instrument);
        if (depth == null)
            return false;

        var limitTicks = TickPrice.FromPrice(command.LimitPrice.Value, depth.TickSize).Ticks;
        return command.Side == Side.Buy
            ? depth.BestAskTick.HasValue && limitTicks >= depth.BestAskTick.Value
            : depth.BestBidTick.HasValue && limitTicks <= depth.BestBidTick.Value;
    }

    private bool CheckAccountConstraints(SubmitOrder command)
    {
        if (_config.AccountType == AccountType.Cash && command.Side == Side.Sell)
        {
            var availablePosition = GetAvailablePositionForSell(command);
            if (command.Quantity.Value > availablePosition)
            {
                EmitConnectorEvent(new OrderRejected(
                    command.OrderId,
                    command.StrategyId,
                    command.VariantId,
                    $"Cash account cannot sell {command.Quantity.Value} with only {availablePosition} available."));
                return false;
            }

            return true;
        }

        if (_config.AccountType == AccountType.Margin
            && command.Side == Side.Sell
            && _config.Margin.ShortSalePolicy == ShortSalePolicy.RequireBorrow)
        {
            var availableShortSaleQuantity = GetAvailableShortSaleQuantity(command);
            if (command.Quantity.Value > availableShortSaleQuantity)
            {
                EmitConnectorEvent(new OrderRejected(
                    command.OrderId,
                    command.StrategyId,
                    command.VariantId,
                    $"Margin account short sale requires borrow/locate: requested {command.Quantity.Value}, available long or located inventory {availableShortSaleQuantity}."));
                return false;
            }
        }

        var required = EstimateCapitalRequirement(command, command.Quantity);
        if (required.IsZero)
        {
            EmitConnectorEvent(new OrderRejected(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                "No price available for account buying power check."));
            return false;
        }

        var available = GetAvailableCash(command.StrategyId, command.VariantId, required.Currency);
        if (required.Amount > available.Amount)
        {
            if (GetVenueSimulationPolicy(command.Instrument.Venue).AllowCashBorrowing)
                return true;

            var account = _config.AccountType == AccountType.Margin ? "margin" : "cash";
            EmitConnectorEvent(new OrderRejected(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                $"Insufficient {account} buying power: required {required.Amount:N2} {required.Currency}, available {available.Amount:N2} {available.Currency}."));
            return false;
        }

        return true;
    }

    private decimal GetAvailablePositionForSell(SubmitOrder command)
    {
        var position = _config.Settlement.CashProceedsDelay > Duration.Zero
            && _config.Settlement.UnsettledSalePolicy == UnsettledSalePolicy.Reject
                ? GetSettledQuantity(command.StrategyId, command.Instrument, command.VariantId).Value
                : GetPositionQuantity(command);
        foreach (var open in _openOrders.Values)
        {
            if (open.Command.StrategyId == command.StrategyId
                && open.Command.VariantId == command.VariantId
                && open.Command.Instrument == command.Instrument
                && open.Command.Side == Side.Sell)
            {
                position -= open.RemainingQuantity.Value;
            }
        }

        return Math.Max(0m, position);
    }

    private decimal GetAvailableShortSaleQuantity(SubmitOrder command)
    {
        var position = GetPositionQuantity(command);
        var longInventory = Math.Max(0m, position);
        var existingShort = Math.Max(0m, -position);
        var locatedBorrow = _config.Margin.BorrowAvailability.TryGetValue(command.Instrument, out var borrow)
            ? borrow.Value
            : 0m;
        var openSellQuantity = 0m;

        foreach (var open in _openOrders.Values)
        {
            if (open.Command.StrategyId == command.StrategyId
                && open.Command.VariantId == command.VariantId
                && open.Command.Instrument == command.Instrument
                && open.Command.Side == Side.Sell)
            {
                openSellQuantity += open.RemainingQuantity.Value;
            }
        }

        return Math.Max(0m, longInventory + locatedBorrow - existingShort - openSellQuantity);
    }

    private Money GetAvailableCash(StrategyId strategyId, int variantId, Currency currency)
    {
        var cash = GetCashBalance(strategyId, variantId, currency);
        foreach (var open in _openOrders.Values)
        {
            if (open.Command.StrategyId == strategyId
                && open.Command.VariantId == variantId
                && open.ReservedCash.Currency == currency)
            {
                cash -= open.ReservedCash;
            }
        }

        return cash;
    }

    private Money GetPendingSettlementTotal(StrategyId strategyId, int variantId, Currency currency)
    {
        var amount = 0m;
        foreach (var settlement in _pendingSettlements)
        {
            if (settlement.StrategyId == strategyId
                && settlement.VariantId == variantId
                && settlement.Amount.Currency == currency)
            {
                amount += settlement.Amount.Amount;
            }
        }

        return new Money(amount, currency);
    }

    private Money GetReservedCashTotal(StrategyId strategyId, int variantId, Currency currency)
    {
        var amount = 0m;
        foreach (var open in _openOrders.Values)
        {
            if (open.Command.StrategyId == strategyId
                && open.Command.VariantId == variantId
                && open.ReservedCash.Currency == currency)
            {
                amount += open.ReservedCash.Amount;
            }
        }

        return new Money(amount, currency);
    }

    private Money GetCashBalance(StrategyId strategyId, int variantId, Currency currency)
    {
        var key = (strategyId, variantId, currency);
        if (_cashBalances.TryGetValue(key, out var cash))
            return cash;

        return currency == _initialCash.Currency
            ? _initialCash
            : Money.Zero(currency);
    }

    private void SetCashBalance(StrategyId strategyId, int variantId, Money cash)
        => _cashBalances[(strategyId, variantId, cash.Currency)] = cash;

    private bool TryValidateAccountTransfer(AccountTransferCommand command, out string reason)
    {
        reason = string.Empty;
        switch (command.TransferType)
        {
            case AccountTransferType.CashDeposit:
            case AccountTransferType.CashWithdrawal:
                if (!command.CashAmount.HasValue || command.CashAmount.Value.Amount <= 0m)
                {
                    reason = $"{command.TransferType} requires a positive cash amount.";
                    return false;
                }

                return true;

            case AccountTransferType.AssetDeposit:
            case AccountTransferType.AssetWithdrawal:
                if (!command.Instrument.HasValue || command.Quantity.Value <= 0m)
                {
                    reason = $"{command.TransferType} requires an instrument and positive quantity.";
                    return false;
                }

                if (!command.CarryingPrice.HasValue || command.CarryingPrice.Value.Value < 0m)
                {
                    reason = $"{command.TransferType} requires a non-negative carrying price.";
                    return false;
                }

                return true;

            case AccountTransferType.InternalTransfer:
                if (!command.DestinationStrategyId.HasValue)
                {
                    reason = "InternalTransfer requires a destination strategy id.";
                    return false;
                }

                if (command.DestinationStrategyId.Value == command.StrategyId
                    && command.DestinationVariantId == command.VariantId)
                {
                    reason = "InternalTransfer source and destination must be different account slices.";
                    return false;
                }

                if (command.CashAmount.HasValue && command.CashAmount.Value.Amount > 0m)
                    return true;

                if (command.Instrument.HasValue
                    && command.Quantity.Value > 0m
                    && command.CarryingPrice.HasValue
                    && command.CarryingPrice.Value.Value >= 0m)
                {
                    return true;
                }

                reason = "InternalTransfer requires either a positive cash amount or an instrument, positive quantity, and non-negative carrying price.";
                return false;

            default:
                reason = $"Transfer type {command.TransferType} is not supported.";
                return false;
        }
    }

    private bool TryApplyCompletedAccountTransfer(AccountTransferCommand command, out string reason)
    {
        reason = string.Empty;
        switch (command.TransferType)
        {
            case AccountTransferType.CashDeposit:
            {
                var amount = command.CashAmount!.Value;
                var cash = GetCashBalance(command.StrategyId, command.VariantId, amount.Currency);
                SetCashBalance(command.StrategyId, command.VariantId, cash + amount);
                EmitAccountStatement(command.StrategyId, command.VariantId, amount.Currency);
                return true;
            }

            case AccountTransferType.CashWithdrawal:
            {
                var amount = command.CashAmount!.Value;
                var available = GetAvailableCash(command.StrategyId, command.VariantId, amount.Currency);
                if (amount.Amount > available.Amount)
                {
                    reason = $"Cash withdrawal requires {amount.Amount:N2} {amount.Currency}, available {available.Amount:N2} {available.Currency}.";
                    return false;
                }

                var cash = GetCashBalance(command.StrategyId, command.VariantId, amount.Currency);
                SetCashBalance(command.StrategyId, command.VariantId, cash - amount);
                EmitAccountStatement(command.StrategyId, command.VariantId, amount.Currency);
                return true;
            }

            case AccountTransferType.AssetDeposit:
                ApplyAssetTransfer(command.StrategyId, command.VariantId, command.Instrument!.Value, command.Quantity, command.CarryingPrice!.Value);
                return true;

            case AccountTransferType.AssetWithdrawal:
            {
                var settled = GetSettledQuantity(command.StrategyId, command.Instrument!.Value, command.VariantId);
                if (command.Quantity.Value > settled.Value)
                {
                    reason = $"Asset withdrawal requires {command.Quantity.Value} settled units, available {settled.Value}.";
                    return false;
                }

                ApplyAssetTransfer(command.StrategyId, command.VariantId, command.Instrument!.Value, new Qty(-command.Quantity.Value), command.CarryingPrice!.Value);
                return true;
            }

            case AccountTransferType.InternalTransfer:
                return TryApplyInternalTransfer(command, out reason);

            default:
                reason = $"Transfer type {command.TransferType} is not supported.";
                return false;
        }
    }

    private bool TryApplyInternalTransfer(AccountTransferCommand command, out string reason)
    {
        reason = string.Empty;
        var destinationStrategyId = command.DestinationStrategyId!.Value;
        var destinationVariantId = command.DestinationVariantId;

        if (command.CashAmount.HasValue)
        {
            var amount = command.CashAmount.Value;
            var available = GetAvailableCash(command.StrategyId, command.VariantId, amount.Currency);
            if (amount.Amount > available.Amount)
            {
                reason = $"Internal cash transfer requires {amount.Amount:N2} {amount.Currency}, available {available.Amount:N2} {available.Currency}.";
                return false;
            }

            var sourceCash = GetCashBalance(command.StrategyId, command.VariantId, amount.Currency);
            var destinationCash = GetCashBalance(destinationStrategyId, destinationVariantId, amount.Currency);
            SetCashBalance(command.StrategyId, command.VariantId, sourceCash - amount);
            SetCashBalance(destinationStrategyId, destinationVariantId, destinationCash + amount);
            EmitAccountStatement(command.StrategyId, command.VariantId, amount.Currency);
            EmitAccountStatement(destinationStrategyId, destinationVariantId, amount.Currency);
            return true;
        }

        var instrument = command.Instrument!.Value;
        var settled = GetSettledQuantity(command.StrategyId, instrument, command.VariantId);
        if (command.Quantity.Value > settled.Value)
        {
            reason = $"Internal asset transfer requires {command.Quantity.Value} settled units, available {settled.Value}.";
            return false;
        }

        var carryingPrice = command.CarryingPrice!.Value;
        ApplyAssetTransfer(command.StrategyId, command.VariantId, instrument, new Qty(-command.Quantity.Value), carryingPrice);
        ApplyAssetTransfer(destinationStrategyId, destinationVariantId, instrument, command.Quantity, carryingPrice);
        return true;
    }

    private void ApplyAssetTransfer(
        StrategyId strategyId,
        int variantId,
        Instrument instrument,
        Qty quantityDelta,
        Price carryingPrice)
    {
        var key = (strategyId, instrument, variantId);
        if (!_positions.TryGetValue(key, out var position))
        {
            position = Position.Empty(instrument);
            _positions[key] = position;
        }

        position.ApplyTransfer(quantityDelta, carryingPrice);
        if (quantityDelta.Value > 0m)
            AddSettledQuantity(strategyId, instrument, variantId, quantityDelta);
        else
            SubtractSettledQuantity(strategyId, instrument, variantId, new Qty(-quantityDelta.Value));

        EmitCustodyPositionSnapshot(
            strategyId,
            instrument,
            variantId,
            position,
            carryingPrice.Currency == default ? _initialCash.Currency : carryingPrice.Currency,
            carryingPrice);
        EmitAccountStatement(
            strategyId,
            variantId,
            carryingPrice.Currency == default ? _initialCash.Currency : carryingPrice.Currency);
    }

    private void ApplyStockSplit(CorporateActionCommand command, Instant effectiveAt)
    {
        if (command.SplitRatio <= 0m)
            throw new ArgumentOutOfRangeException(nameof(command), "Stock split requires a positive split ratio.");

        var affected = _positions
            .Where(entry => entry.Key.Instrument == command.Instrument && !entry.Value.IsFlat)
            .ToArray();

        foreach (var ((strategyId, instrument, variantId), position) in affected)
        {
            var quantityBefore = position.Quantity;
            var avgBefore = position.AvgEntryPrice;
            position.ApplySplit(command.SplitRatio);
            ScaleSettledQuantity(strategyId, instrument, variantId, command.SplitRatio);
            ScalePendingAssetDeliveries(strategyId, instrument, variantId, command.SplitRatio);

            EmitConnectorEvent(new CorporateActionEffectSnapshot(
                command.CorporateActionId,
                command.ActionType,
                strategyId,
                variantId,
                instrument,
                quantityBefore,
                position.Quantity,
                avgBefore,
                position.AvgEntryPrice,
                CashAmount: null,
                effectiveAt)
            {
                Time = effectiveAt
            });

            var currency = position.AvgEntryPrice.Currency == default
                ? _initialCash.Currency
                : position.AvgEntryPrice.Currency;
            EmitCustodyPositionSnapshot(strategyId, instrument, variantId, position, currency, position.AvgEntryPrice);
            EmitAccountStatement(strategyId, variantId, currency);
        }
    }

    private void ApplyCashDividend(CorporateActionCommand command, Instant effectiveAt)
    {
        var dividend = command.DividendPerShare
            ?? throw new ArgumentOutOfRangeException(nameof(command), "Cash dividend requires a dividend per share.");
        if (dividend.Amount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(command), "Cash dividend requires a positive dividend per share.");

        var affected = _positions
            .Where(entry => entry.Key.Instrument == command.Instrument && !entry.Value.IsFlat)
            .ToArray();

        foreach (var ((strategyId, instrument, variantId), position) in affected)
        {
            var settled = GetSettledQuantity(strategyId, instrument, variantId);
            if (settled.Value <= 0m)
                continue;

            var cashAmount = new Money(settled.Value * dividend.Amount, dividend.Currency);
            var cash = GetCashBalance(strategyId, variantId, dividend.Currency);
            SetCashBalance(strategyId, variantId, cash + cashAmount);

            EmitConnectorEvent(new CorporateActionEffectSnapshot(
                command.CorporateActionId,
                command.ActionType,
                strategyId,
                variantId,
                instrument,
                position.Quantity,
                position.Quantity,
                position.AvgEntryPrice,
                position.AvgEntryPrice,
                cashAmount,
                effectiveAt)
            {
                Time = effectiveAt
            });
            EmitPerformanceSnapshot(strategyId, variantId, dividend.Currency);
            EmitAccountStatement(strategyId, variantId, dividend.Currency);
        }
    }

    private void ScaleSettledQuantity(
        StrategyId strategyId,
        Instrument instrument,
        int variantId,
        decimal splitRatio)
    {
        var key = (strategyId, instrument, variantId);
        if (_settledPositions.TryGetValue(key, out var settled))
            _settledPositions[key] = new Qty(settled.Value * splitRatio);
    }

    private void ScalePendingAssetDeliveries(
        StrategyId strategyId,
        Instrument instrument,
        int variantId,
        decimal splitRatio)
    {
        for (var i = 0; i < _pendingAssetDeliveries.Count; i++)
        {
            var delivery = _pendingAssetDeliveries[i];
            if (delivery.StrategyId == strategyId
                && delivery.Instrument == instrument
                && delivery.VariantId == variantId)
            {
                _pendingAssetDeliveries[i] = delivery with { Quantity = new Qty(delivery.Quantity.Value * splitRatio) };
            }
        }
    }

    private void EmitAccountTransferRequested(AccountTransferCommand command)
    {
        var now = GetCashEventTime();
        EmitConnectorEvent(new AccountTransferRequested(
            command.TransferId,
            command.StrategyId,
            command.VariantId,
            command.TransferType,
            command.CashAmount,
            command.Instrument,
            command.Quantity,
            now,
            command.ExternalReference,
            command.DestinationStrategyId,
            command.DestinationVariantId,
            Venue: command.Instrument?.Venue,
            CarryingPrice: command.CarryingPrice)
        {
            Time = now
        });
    }

    private void EmitAccountTransferFailed(AccountTransferCommand command, string reason)
    {
        var now = GetCashEventTime();
        EmitConnectorEvent(new AccountTransferFailed(
            command.TransferId,
            command.StrategyId,
            command.VariantId,
            command.TransferType,
            command.CashAmount,
            command.Instrument,
            command.Quantity,
            now,
            reason,
            command.ExternalReference,
            command.DestinationStrategyId,
            command.DestinationVariantId,
            Venue: command.Instrument?.Venue,
            CarryingPrice: command.CarryingPrice)
        {
            Time = now
        });
        EmitAccountTransferStatus(command, AccountTransferStatus.Failed, reason);
    }

    private void EmitAccountTransferStatus(
        AccountTransferCommand command,
        AccountTransferStatus status,
        string? reason)
    {
        var now = GetCashEventTime();
        EmitConnectorEvent(new AccountTransferStatusSnapshot(
            command.TransferId,
            command.StrategyId,
            command.VariantId,
            command.TransferType,
            status,
            command.CashAmount,
            command.Instrument,
            command.Quantity,
            now,
            reason,
            command.ExternalReference,
            command.DestinationStrategyId,
            command.DestinationVariantId,
            Venue: command.Instrument?.Venue,
            CarryingPrice: command.CarryingPrice)
        {
            Time = now
        });
    }

    private Money EstimateCapitalRequirement(SubmitOrder command)
        => EstimateCapitalRequirement(command, command.Quantity);

    private Money EstimateCapitalRequirement(SubmitOrder command, Qty quantity)
    {
        if (quantity.Value <= 0m)
            return Money.Zero(_initialCash.Currency);

        if (command.Type == OrderType.Market && TryEstimateMarketFillCost(command, quantity, out var marketCost))
            return marketCost;

        return TryGetCashCheckPrice(command, out var price)
            ? EstimateCapitalRequirement(command, quantity, price)
            : Money.Zero(_initialCash.Currency);
    }

    private Money EstimateCapitalRequirement(SubmitOrder command, Qty quantity, Price price)
    {
        var contract = GetContract(command.Instrument);
        var signedQuantity = ToSignedQuantity(command.Side, quantity);
        var initialMargin = GetInitialMarginRequirement(contract, signedQuantity, price, null);
        var upfrontCash = GetUpfrontCashFlow(contract, quantity, price);
        var commission = CalculateCommission(command.StrategyId, command.VariantId, command.Instrument, command.Side, quantity, price, IsPassiveOrder(command));
        return _config.AccountType == AccountType.Margin
            ? initialMargin + commission
            : command.Side == Side.Buy
                ? upfrontCash + commission
                : Money.Zero(price.Currency);
    }

    private Money CalculateCommission(
        StrategyId strategyId,
        int variantId,
        Instrument instrument,
        Side side,
        Qty quantity,
        Price price,
        bool isMaker)
    {
        var contract = GetContract(instrument);
        var thirtyDayVolume = GetThirtyDayFeeVolume(strategyId, variantId, contract.Exposure.SettlementCurrency(), GetCashEventTime());
        return _config.Fees.Calculate(contract, quantity, price, side, isMaker, thirtyDayVolume, _valuation);
    }

    private Price ApplyPriceImprovement(Price price, Side side, bool isMaker)
        => _config.PriceImprovement.Apply(price, side, isMaker);

    private void TrackFeeNotional(
        StrategyId strategyId,
        int variantId,
        Instrument instrument,
        Qty quantity,
        Price price)
    {
        if (quantity.Value <= 0m || price.Value <= 0m)
            return;

        var contract = GetContract(instrument);
        var notional = _valuation.Notional(contract, quantity, price);
        _feeNotionalHistory.Add(new AccountTradeNotional(
            strategyId,
            variantId,
            notional,
            GetCashEventTime()));
    }

    private Money GetThirtyDayFeeVolume(
        StrategyId strategyId,
        int variantId,
        Currency currency,
        Instant now)
    {
        var cutoff = now - Duration.FromDays(30);
        var amount = 0m;
        for (var i = _feeNotionalHistory.Count - 1; i >= 0; i--)
        {
            var item = _feeNotionalHistory[i];
            if (item.TradedAt < cutoff)
            {
                _feeNotionalHistory.RemoveAt(i);
                continue;
            }

            if (item.StrategyId == strategyId
                && item.VariantId == variantId
                && item.Notional.Currency == currency)
            {
                amount += item.Notional.Amount;
            }
        }

        return new Money(amount, currency);
    }

    private bool TryEstimateMarketFillCost(SubmitOrder command, Qty quantity, out Money cost)
    {
        cost = Money.Zero(_initialCash.Currency);
        var remaining = quantity.Value;
        foreach (var passiveOrderId in _restingBook.GetMarketOrderIds(command, _openOrders))
        {
            if (remaining <= 0m)
                break;

            var passive = _openOrders[passiveOrderId];
            if (!passive.Command.LimitPrice.HasValue)
                continue;

            var visibleQuantity = passive.DisplayRemaining ?? passive.RemainingQuantity;
            var fillQuantity = new Qty(Math.Min(remaining, Math.Min(passive.RemainingQuantity.Value, visibleQuantity.Value)));
            if (fillQuantity.Value <= 0m)
                continue;

            var fillPrice = passive.Command.LimitPrice.Value;
            var passiveContract = GetContract(passive.Command.Instrument);
            var passiveSignedQuantity = ToSignedQuantity(passive.Command.Side, fillQuantity);
            var passiveInitialMargin = GetInitialMarginRequirement(passiveContract, passiveSignedQuantity, fillPrice, null);
            var passiveUpfrontCash = GetUpfrontCashFlow(passiveContract, fillQuantity, fillPrice);
            var passiveCommission = CalculateCommission(
                passive.Command.StrategyId,
                passive.Command.VariantId,
                passive.Command.Instrument,
                passive.Command.Side,
                fillQuantity,
                fillPrice,
                isMaker: false);
            cost += _config.AccountType == AccountType.Margin
                ? passiveInitialMargin + passiveCommission
                : command.Side == Side.Buy
                    ? passiveUpfrontCash + passiveCommission
                    : Money.Zero(fillPrice.Currency);
            remaining -= fillQuantity.Value;
        }

        if (remaining <= 0m)
            return !cost.IsZero;

        var depth = _depths.GetValueOrDefault(command.Instrument);
        if (depth == null)
            return !cost.IsZero;

        Span<Rhodium.HFT.DepthLevel> levels = stackalloc Rhodium.HFT.DepthLevel[MaxReplayBookLevels];
        var liquiditySide = command.Side == Side.Buy ? Side.Sell : Side.Buy;
        var levelCount = depth.CopyLevels(liquiditySide, levels);
        if (levelCount == 0)
            return !cost.IsZero;

        var initialMargin = Money.Zero(Currency.USD);
        var upfrontCash = Money.Zero(Currency.USD);
        var commission = Money.Zero(Currency.USD);
        var contract = GetContract(command.Instrument);
        var signedSide = command.Side;
        for (var i = 0; i < levelCount && remaining > 0m; i++)
        {
            var level = levels[i];
            var fillQuantity = new Qty(Math.Min(remaining, level.Quantity));
            var fillPrice = new Price(level.PriceTick * depth.TickSize, Currency.USD);
            fillPrice = ApplyPriceImprovement(fillPrice, command.Side, isMaker: false);
            var slippageMoney = _config.Slippage.Calculate(fillPrice, fillQuantity, command.Side);
            fillPrice = new Price(fillPrice.Value + slippageMoney.Amount, fillPrice.Currency);
            initialMargin += GetInitialMarginRequirement(contract, ToSignedQuantity(signedSide, fillQuantity), fillPrice, null);
            upfrontCash += GetUpfrontCashFlow(contract, fillQuantity, fillPrice);
            commission += CalculateCommission(command.StrategyId, command.VariantId, command.Instrument, command.Side, fillQuantity, fillPrice, isMaker: false);
            remaining -= fillQuantity.Value;
        }

        var depthCost = _config.AccountType == AccountType.Margin
            ? initialMargin + commission
            : command.Side == Side.Buy
                ? upfrontCash + commission
                : Money.Zero(upfrontCash.Currency);
        cost += depthCost;

        return !cost.IsZero;
    }

    private static bool IsPassiveOrder(SubmitOrder command)
        => command.Type is OrderType.Limit or OrderType.StopLimit or OrderType.LimitIfTouched or OrderType.TrailingStopLimit;

    private bool TryGetCashCheckPrice(SubmitOrder command, out Price price)
    {
        if (command.LimitPrice.HasValue)
        {
            price = command.LimitPrice.Value;
            return price.Value > 0m;
        }

        if (command.StopPrice.HasValue)
        {
            price = command.StopPrice.Value;
            return price.Value > 0m;
        }

        var depth = _depths.GetValueOrDefault(command.Instrument);
        var tick = command.Side == Side.Buy
            ? depth?.BestAskTick
            : depth?.BestBidTick;
        if (depth != null && tick.HasValue)
        {
            price = new Price(tick.Value * depth.TickSize, Currency.USD);
            return true;
        }

        price = default;
        return false;
    }

    private Task SubmitMarketOrderAsync(SubmitOrder command)
    {
        if (_events == null)
            throw new InvalidOperationException("Connector not started");

        if (command.TimeInForce == TimeInForce.FOK
            && GetMarketAvailableQuantity(command) < command.Quantity.Value)
        {
            EmitConnectorEvent(new OrderCancelled(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                command.Quantity,
                "FOK market order was not fully fillable against replay liquidity."));
            return Task.CompletedTask;
        }

        var incoming = new SimulatedOrder
        {
            Command = command,
            RemainingQuantity = command.Quantity,
            ReservedCash = Money.Zero(_initialCash.Currency),
            SubmitTime = GetCashEventTime(),
            QueuePosition = 0m,
            DisplayRemaining = null,
            VenueSequence = NextVenueSequence()
        };
        MatchMarketRestingOrders(command.OrderId, incoming);
        var remaining = incoming.RemainingQuantity.Value;
        if (remaining <= 0m)
            return Task.CompletedTask;

        var depth = _depths.GetValueOrDefault(command.Instrument);
        if (depth == null)
        {
            EmitConnectorEvent(new OrderCancelled(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                new Qty(remaining),
                "Market order exhausted available replay book liquidity."));
            return Task.CompletedTask;
        }

        Span<Rhodium.HFT.DepthLevel> levels = stackalloc Rhodium.HFT.DepthLevel[MaxReplayBookLevels];
        var liquiditySide = command.Side == Side.Buy ? Side.Sell : Side.Buy;
        var levelCount = depth.CopyLevels(liquiditySide, levels);
        if (levelCount == 0)
        {
            EmitConnectorEvent(new OrderCancelled(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                new Qty(remaining),
                "Market order exhausted available replay book liquidity."));
            return Task.CompletedTask;
        }

        var simulationPolicy = GetVenueSimulationPolicy(command.Instrument.Venue);
        var referencePriceTick = levels[0].PriceTick;
        for (var i = 0; i < levelCount && remaining > 0m; i++)
        {
            var level = levels[i];
            if (!IsWithinPriceProtection(command.Side, level.PriceTick, referencePriceTick, simulationPolicy.PriceProtectionTicks))
                break;

            var fillQuantity = new Qty(Math.Min(remaining, level.Quantity));
            var fillPrice = new Price(level.PriceTick * depth.TickSize, Currency.USD);

            fillPrice = ApplyPriceImprovement(fillPrice, command.Side, isMaker: false);
            var slippageMoney = _config.Slippage.Calculate(fillPrice, fillQuantity, command.Side);
            fillPrice = new Price(fillPrice.Value + slippageMoney.Amount, fillPrice.Currency);

            var commission = CalculateCommission(command.StrategyId, command.VariantId, command.Instrument, command.Side, fillQuantity, fillPrice, isMaker: false);

            EmitConnectorEvent(new OrderFilled(
                command.OrderId,
                command.Instrument,
                command.VariantId,
                command.StrategyId,
                command.Side,
                fillQuantity,
                fillPrice,
                commission));
            remaining -= fillQuantity.Value;
            EmitOrderFillState(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                fillQuantity,
                new Qty(remaining));
            TrackFeeNotional(command.StrategyId, command.VariantId, command.Instrument, fillQuantity, fillPrice);
            ApplyCashFill(command.StrategyId, command.Instrument, command.VariantId, command.Side, fillQuantity, fillPrice, commission);
            ApplyFill(
                command.StrategyId,
                command.Instrument,
                command.VariantId,
                command.Side,
                fillQuantity,
                fillPrice,
                commission);
            if (simulationPolicy.LiquidityConsumption)
                DepleteExternalDepth(depth, liquiditySide, level, fillQuantity);
        }

        if (remaining > 0m)
        {
            EmitConnectorEvent(new OrderCancelled(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                new Qty(remaining),
                "Market order exhausted available replay book liquidity."));
        }

        return Task.CompletedTask;
    }

    private void DepleteExternalDepth(
        IHftDepth depth,
        Side side,
        Rhodium.HFT.DepthLevel level,
        Qty filledQuantity)
    {
        var remainingQuantity = Math.Max(0m, level.Quantity - filledQuantity.Value);
        depth.Update(side, level.PriceTick, remainingQuantity, GetCashEventTime());
    }

    private static bool IsWithinPriceProtection(
        Side orderSide,
        long priceTick,
        long referencePriceTick,
        int protectionTicks)
    {
        if (protectionTicks <= 0)
            return true;

        return orderSide == Side.Buy
            ? priceTick <= referencePriceTick + protectionTicks
            : priceTick >= referencePriceTick - protectionTicks;
    }

    private decimal GetMarketAvailableQuantity(SubmitOrder command)
    {
        var available = _restingBook.GetMarketAvailableQuantity(command, _openOrders);
        var depth = _depths.GetValueOrDefault(command.Instrument);
        if (depth == null)
            return available;

        Span<Rhodium.HFT.DepthLevel> levels = stackalloc Rhodium.HFT.DepthLevel[MaxReplayBookLevels];
        var liquiditySide = command.Side == Side.Buy ? Side.Sell : Side.Buy;
        var levelCount = depth.CopyLevels(liquiditySide, levels);
        for (var i = 0; i < levelCount; i++)
        {
            available += levels[i].Quantity;
            if (available >= command.Quantity.Value)
                break;
        }

        return available;
    }

    public Task CancelOrderAsync(CancelOrder command, CancellationToken ct)
    {
        if (_events == null)
            throw new InvalidOperationException("Connector not started");

        if (CancelInflightSubmission(command))
            return Task.CompletedTask;

        if (CancelStagedOtoChild(command))
            return Task.CompletedTask;

        if (_config.Latency.EntryMean > Duration.Zero && _openOrders.ContainsKey(command.OrderId))
        {
            EnqueueInflightCancel(command, _config.Latency.EntryMean);
            return Task.CompletedTask;
        }

        ProcessCancelOrder(command);
        return Task.CompletedTask;
    }

    private void ProcessCancelOrder(CancelOrder command)
    {
        if (_openOrders.TryGetValue(command.OrderId, out var order))
        {
            RemoveOpenOrder(command.OrderId);
            EmitConnectorEvent(new OrderCancelled(
                command.OrderId,
                order.Command.StrategyId,
                order.Command.VariantId,
                order.RemainingQuantity,
                "Cancelled by user"));
        }
    }

    private bool CancelInflightSubmission(CancelOrder command)
    {
        foreach (var pending in _inflightCommands.ToArray())
        {
            if (pending.Kind != InflightCommandKind.Submit
                || pending.SubmitCommand!.Value.OrderId != command.OrderId)
            {
                continue;
            }

            _inflightCommands.Remove(pending);
            var pendingCommand = pending.SubmitCommand.Value;
            EmitConnectorEvent(new OrderCancelled(
                command.OrderId,
                pendingCommand.StrategyId,
                pendingCommand.VariantId,
                pendingCommand.Quantity,
                "Cancelled before exchange arrival."));
            return true;
        }

        return false;
    }

    private bool CancelStagedOtoChild(CancelOrder command)
    {
        foreach (var (orderListId, children) in _stagedOtoChildren.ToArray())
        {
            var childIndex = children.FindIndex(child => child.OrderId == command.OrderId);
            if (childIndex < 0)
                continue;

            var child = children[childIndex];
            children.RemoveAt(childIndex);
            if (children.Count == 0)
                _stagedOtoChildren.Remove(orderListId);

            EmitConnectorEvent(new OrderCancelled(
                child.OrderId,
                child.StrategyId,
                child.VariantId,
                child.Quantity,
                "Cancelled while staged behind OTO parent."));
            return true;
        }

        return false;
    }

    public Task ModifyOrderAsync(ModifyOrder command, CancellationToken ct)
    {
        if (_events == null)
            throw new InvalidOperationException("Connector not started");

        if (ModifyInflightSubmission(command))
            return Task.CompletedTask;

        if (ModifyStagedOtoChild(command))
            return Task.CompletedTask;

        if (_config.Latency.EntryMean > Duration.Zero && _openOrders.ContainsKey(command.OrderId))
        {
            EnqueueInflightModify(command, _config.Latency.EntryMean);
            return Task.CompletedTask;
        }

        ProcessModifyOrder(command);
        return Task.CompletedTask;
    }

    private void ProcessModifyOrder(ModifyOrder command)
    {
        if (_openOrders.TryGetValue(command.OrderId, out var order))
        {
            var oldLimitPrice = order.Command.LimitPrice;
            var oldRemainingQuantity = order.RemainingQuantity;
            var losesPriority = false;
            if (command.NewQuantity.HasValue)
            {
                var filledQuantity = order.Command.Quantity - order.RemainingQuantity;
                order.Command = order.Command with { Quantity = command.NewQuantity.Value };
                order.RemainingQuantity = new Qty(Math.Max(0m, command.NewQuantity.Value.Value - filledQuantity.Value));
                RefreshDisplayQuantity(order);
                order.ReservedCash = EstimateCapitalRequirement(order.Command, order.RemainingQuantity);
                if (order.RemainingQuantity.Value > oldRemainingQuantity.Value)
                    losesPriority = true;
            }
            if (command.NewLimitPrice.HasValue)
            {
                order.Command = order.Command with { LimitPrice = command.NewLimitPrice.Value };
                order.ReservedCash = EstimateCapitalRequirement(order.Command, order.RemainingQuantity);
                // Price change resets queue position
                order.QueuePosition = _config.QueueModel.GetInitialPosition();
                if (oldLimitPrice != command.NewLimitPrice)
                    losesPriority = true;
            }

            if (losesPriority)
                order.VenueSequence = NextVenueSequence();

            _restingBook.AddOrUpdate(command.OrderId, order, losesPriority);

            EmitConnectorEvent(new OrderModified(
                command.OrderId,
                order.Command.StrategyId,
                order.Command.VariantId,
                command.NewQuantity,
                command.NewLimitPrice));
        }
    }

    private bool ModifyInflightSubmission(ModifyOrder command)
    {
        foreach (var pending in _inflightCommands.ToArray())
        {
            if (pending.Kind != InflightCommandKind.Submit
                || pending.SubmitCommand!.Value.OrderId != command.OrderId)
            {
                continue;
            }

            var updated = pending.SubmitCommand.Value;
            if (command.NewQuantity.HasValue)
                updated = updated with { Quantity = command.NewQuantity.Value };
            if (command.NewLimitPrice.HasValue)
                updated = updated with { LimitPrice = command.NewLimitPrice.Value };

            _inflightCommands.Remove(pending);
            _inflightCommands.Add(pending with { SubmitCommand = updated });
            EmitConnectorEvent(new OrderModified(
                command.OrderId,
                updated.StrategyId,
                updated.VariantId,
                command.NewQuantity,
                command.NewLimitPrice));
            return true;
        }

        return false;
    }

    private bool ModifyStagedOtoChild(ModifyOrder command)
    {
        foreach (var children in _stagedOtoChildren.Values)
        {
            var childIndex = children.FindIndex(child => child.OrderId == command.OrderId);
            if (childIndex < 0)
                continue;

            var updated = children[childIndex];
            if (command.NewQuantity.HasValue)
                updated = updated with { Quantity = command.NewQuantity.Value };
            if (command.NewLimitPrice.HasValue)
                updated = updated with { LimitPrice = command.NewLimitPrice.Value };

            children[childIndex] = updated;
            EmitConnectorEvent(new OrderModified(
                command.OrderId,
                updated.StrategyId,
                updated.VariantId,
                command.NewQuantity,
                command.NewLimitPrice));
            return true;
        }

        return false;
    }

    private void UpdateDepth(FinanceEvent evt)
    {
        if (evt is QuoteReceived quote)
        {
            if (_depths.TryGetValue(quote.Instrument, out var depth))
            {
                depth.Clear();
                depth.Update(Side.Buy, quote.Quote.BidTick(depth.TickSize).Ticks, quote.Quote.BidSize.Value, quote.Quote.Time.ExchangeTime);
                depth.Update(Side.Sell, quote.Quote.AskTick(depth.TickSize).Ticks, quote.Quote.AskSize.Value, quote.Quote.Time.ExchangeTime);
            }
        }
        else if (evt is BookSnapshotReceived book)
        {
            if (_depths.TryGetValue(book.Instrument, out var depth))
            {
                depth.Clear();

                foreach (var level in book.Book.Bids)
                    depth.Update(Side.Buy, TickPrice.FromPrice(level.Price, depth.TickSize).Ticks, level.Size.Value, book.Book.Time);

                foreach (var level in book.Book.Asks)
                    depth.Update(Side.Sell, TickPrice.FromPrice(level.Price, depth.TickSize).Ticks, level.Size.Value, book.Book.Time);
            }
        }
        else if (evt is BookLevelDeltaReceived delta)
        {
            if (_depths.TryGetValue(delta.Instrument, out var depth))
                ApplyBookLevelDelta(depth, delta.Delta, delta.Time);
        }
        else if (evt is BookLevelDeltasReceived deltas)
        {
            if (_depths.TryGetValue(deltas.Instrument, out var depth))
            {
                foreach (var bookDelta in deltas.Deltas)
                    ApplyBookLevelDelta(depth, bookDelta, deltas.Time);
            }
        }
        else if (evt is BookDepthSnapshotReceived snapshot)
        {
            if (_depths.TryGetValue(snapshot.Instrument, out var depth))
            {
                depth.Clear();

                foreach (var level in snapshot.Bids.Take(snapshot.Depth))
                    depth.Update(Side.Buy, TickPrice.FromPrice(level.Price, depth.TickSize).Ticks, level.Size.Value, snapshot.Time);

                foreach (var level in snapshot.Asks.Take(snapshot.Depth))
                    depth.Update(Side.Sell, TickPrice.FromPrice(level.Price, depth.TickSize).Ticks, level.Size.Value, snapshot.Time);
            }
        }
        else if (evt is BookDepth10Received depth10)
        {
            if (_depths.TryGetValue(depth10.Instrument, out var depth))
            {
                depth.Clear();

                foreach (var level in depth10.Bids.Take(10))
                    depth.Update(Side.Buy, TickPrice.FromPrice(level.Price, depth.TickSize).Ticks, level.Size.Value, depth10.Time);

                foreach (var level in depth10.Asks.Take(10))
                    depth.Update(Side.Sell, TickPrice.FromPrice(level.Price, depth.TickSize).Ticks, level.Size.Value, depth10.Time);
            }
        }
    }

    private static void ApplyBookLevelDelta(IHftDepth depth, BookLevelDelta delta, Instant time)
    {
        if (delta.Action == BookAction.Clear)
        {
            depth.Clear(delta.Side);
            return;
        }

        var quantity = delta.Action == BookAction.Delete
            ? 0m
            : delta.Size.Value;
        depth.Update(delta.Side, TickPrice.FromPrice(delta.Price, depth.TickSize).Ticks, quantity, time);
    }

    private void UpdateMarketStatus(FinanceEvent evt)
    {
        switch (evt)
        {
            case VenueStatusChanged venueStatus:
                _venueStatuses[venueStatus.Venue] = venueStatus.Status;
                break;
            case InstrumentStatusChanged instrumentStatus:
                _instrumentStatuses[instrumentStatus.Instrument] = instrumentStatus.Status;
                break;
            case InstrumentClosed instrumentClosed:
                _instrumentStatuses[instrumentClosed.Instrument] = MarketStatus.Closed;
                _closingMarks[instrumentClosed.Instrument] = instrumentClosed.ClosePrice;
                break;
            case MarketOpened opened:
                _venueStatuses[opened.Venue] = MarketStatus.Open;
                break;
            case MarketClosed closed:
                _venueStatuses[closed.Venue] = MarketStatus.Closed;
                break;
            case PreMarketOpened preMarket:
                _venueStatuses[preMarket.Venue] = MarketStatus.PreOpen;
                break;
            case PostMarketOpened postMarket:
                _venueStatuses[postMarket.Venue] = MarketStatus.Closed;
                break;
        }
    }

    private void CheckFills(FinanceEvent evt)
    {
        if (_events == null) return;

        _filledOrdersBuffer.Clear();
        var remainingTradeQuantity = _config.FillBehavior == FillBehavior.PartialFillOnTrade && evt is TradeOccurred tradeEvent
            ? tradeEvent.Trade.Size.Value
            : decimal.MaxValue;

        foreach (var (orderId, order) in GetFillScanOrders(evt))
        {
            if (_filledOrdersBuffer.Contains(orderId))
                continue;

            if (!IsMarketEventForOrder(evt, order.Command.Instrument))
                continue;

            if (!CanExecuteOnMarketEvent(evt, order.Command.Instrument))
                continue;

            if (!IsOpenForTrading(order.Command.Instrument))
                continue;

            var depth = _depths.GetValueOrDefault(order.Command.Instrument);
            if (depth == null) continue;

            if (IsExpired(order, evt))
            {
                EmitConnectorEvent(new OrderExpired(
                    orderId,
                    order.Command.StrategyId,
                    order.Command.VariantId));
                _filledOrdersBuffer.Add(orderId);
                continue;
            }

            // Create fill context
            var limitPriceTick = order.Command.LimitPrice.HasValue
                ? (int)(order.Command.LimitPrice.Value.Value / depth.TickSize)
                : 0;

            var trade = evt is TradeOccurred tradeEvt ? (Trade?)tradeEvt.Trade : null;

            if (TryFillTriggeredOrder(order, evt, out var triggeredFillPrice, out var triggeredMaker))
            {
                EmitFillAndTrack(orderId, order, triggeredFillPrice, DetermineFillQuantity(order, evt, remainingTradeQuantity), triggeredMaker, ref remainingTradeQuantity);
                continue;
            }

            if (order.Command.Type != OrderType.Limit)
                continue;

            var ctx = new FillModelContext
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
                EmitFillAndTrack(orderId, order, fillPrice, DetermineFillQuantity(order, evt, remainingTradeQuantity), isMaker: true, ref remainingTradeQuantity);
            }
            else
            {
                // Advance queue position based on market activity
                if (evt is TradeOccurred queueTradeEvent)
                {
                    order.QueuePosition = AdvanceQueuePosition(
                        order.QueuePosition,
                        queueTradeEvent,
                        order.Command.Side,
                        limitPriceTick);
                }
            }
        }

        // Remove filled orders
        foreach (var orderId in _filledOrdersBuffer)
            RemoveOpenOrder(orderId);
    }

    private KeyValuePair<OrderId, SimulatedOrder>[] GetFillScanOrders(FinanceEvent evt)
    {
        var orders = _openOrders.ToArray();
        if (_config.FillBehavior != FillBehavior.PartialFillOnTrade || evt is not TradeOccurred trade)
            return orders;

        var bookOrderIds = _restingBook.GetPassiveTradeOrderIds(trade, _openOrders).ToArray();
        var bookOrderSet = bookOrderIds.ToHashSet();
        return bookOrderIds
            .Select(orderId => new KeyValuePair<OrderId, SimulatedOrder>(orderId, _openOrders[orderId]))
            .Concat(orders
                .Where(entry => !bookOrderSet.Contains(entry.Key))
                .OrderBy(entry => entry.Value.VenueSequence))
            .ToArray();
    }

    private long NextVenueSequence() => ++_nextVenueSequence;

    private void MatchCrossedRestingOrders(OrderId incomingOrderId, SimulatedOrder incoming)
    {
        if (incoming.Command.Type != OrderType.Limit
            || !incoming.Command.LimitPrice.HasValue
            || incoming.RemainingQuantity.Value <= 0m)
        {
            return;
        }

        foreach (var passiveOrderId in _restingBook.GetCrossingOrderIds(incoming.Command, _openOrders).ToArray())
        {
            if (incoming.RemainingQuantity.Value <= 0m)
                break;

            if (!_openOrders.TryGetValue(passiveOrderId, out var passive)
                || passive.RemainingQuantity.Value <= 0m
                || !passive.Command.LimitPrice.HasValue)
            {
                continue;
            }

            var quantity = new Qty(Math.Min(incoming.RemainingQuantity.Value, passive.RemainingQuantity.Value));
            if (passive.DisplayRemaining.HasValue)
                quantity = new Qty(Math.Min(quantity.Value, passive.DisplayRemaining.Value.Value));
            if (quantity.Value <= 0m)
                continue;

            var fillPrice = passive.Command.LimitPrice.Value;
            var remainingTradeQuantity = decimal.MaxValue;
            EmitFillAndTrack(passiveOrderId, passive, fillPrice, quantity, isMaker: true, ref remainingTradeQuantity);
            EmitFill(incomingOrderId, incoming, fillPrice, quantity, isMaker: false);

            if (incoming.Command.OrderListId.HasValue)
                ApplyOrderListFill(incomingOrderId, incoming);
        }

        foreach (var orderId in _filledOrdersBuffer)
            RemoveOpenOrder(orderId);
        _filledOrdersBuffer.Clear();
    }

    private void MatchMarketRestingOrders(OrderId incomingOrderId, SimulatedOrder incoming)
    {
        if (incoming.Command.Type != OrderType.Market || incoming.RemainingQuantity.Value <= 0m)
            return;

        foreach (var passiveOrderId in _restingBook.GetMarketOrderIds(incoming.Command, _openOrders).ToArray())
        {
            if (incoming.RemainingQuantity.Value <= 0m)
                break;

            if (!_openOrders.TryGetValue(passiveOrderId, out var passive)
                || passive.RemainingQuantity.Value <= 0m
                || !passive.Command.LimitPrice.HasValue)
            {
                continue;
            }

            var quantity = new Qty(Math.Min(incoming.RemainingQuantity.Value, passive.RemainingQuantity.Value));
            if (passive.DisplayRemaining.HasValue)
                quantity = new Qty(Math.Min(quantity.Value, passive.DisplayRemaining.Value.Value));
            if (quantity.Value <= 0m)
                continue;

            var fillPrice = passive.Command.LimitPrice.Value;
            var remainingTradeQuantity = decimal.MaxValue;
            EmitFillAndTrack(passiveOrderId, passive, fillPrice, quantity, isMaker: true, ref remainingTradeQuantity);
            EmitFill(incomingOrderId, incoming, fillPrice, quantity, isMaker: false);

            if (incoming.Command.OrderListId.HasValue)
                ApplyOrderListFill(incomingOrderId, incoming);
        }

        foreach (var orderId in _filledOrdersBuffer)
            RemoveOpenOrder(orderId);
        _filledOrdersBuffer.Clear();
    }

    private static bool IsPassiveTradeCandidate(SubmitOrder command, Trade trade)
    {
        if (command.Type != OrderType.Limit || !command.LimitPrice.HasValue)
            return false;

        return trade.AggressorSide switch
        {
            Side.Sell => command.Side == Side.Buy && command.LimitPrice.Value.Value >= trade.Price.Value,
            Side.Buy => command.Side == Side.Sell && command.LimitPrice.Value.Value <= trade.Price.Value,
            _ => false
        };
    }

    private void EmitFillAndTrack(
        OrderId orderId,
        SimulatedOrder order,
        Price fillPrice,
        Qty fillQuantity,
        bool isMaker,
        ref decimal remainingTradeQuantity)
    {
        if (fillQuantity.Value <= 0m)
            return;

        EmitFill(orderId, order, fillPrice, fillQuantity, isMaker);
        if (remainingTradeQuantity != decimal.MaxValue)
            remainingTradeQuantity = Math.Max(0m, remainingTradeQuantity - fillQuantity.Value);

        ApplyOrderListFill(orderId, order);
        if (order.RemainingQuantity.Value <= 0m)
            _filledOrdersBuffer.Add(orderId);
    }

    private static bool IsMarketEventForOrder(FinanceEvent evt, Instrument instrument)
        => evt switch
        {
            QuoteReceived quote => quote.Instrument == instrument,
            TradeOccurred trade => trade.Instrument == instrument,
            BookSnapshotReceived book => book.Instrument == instrument,
            BookLevelDeltaReceived delta => delta.Instrument == instrument,
            BookLevelDeltasReceived deltas => deltas.Instrument == instrument,
            BookDepthSnapshotReceived depth => depth.Instrument == instrument,
            BookDepth10Received depth => depth.Instrument == instrument,
            BarClosed bar => bar.Instrument == instrument,
            _ => true
        };

    private bool CanExecuteOnMarketEvent(FinanceEvent evt, Instrument instrument)
    {
        var policy = GetVenueSimulationPolicy(instrument.Venue);
        return evt switch
        {
            BarClosed => policy.BarExecution,
            TradeOccurred => policy.TradeExecution,
            _ => true
        };
    }

    private void ApplyOrderListFill(OrderId filledOrderId, SimulatedOrder filledOrder)
    {
        if (!filledOrder.Command.OrderListId.HasValue)
            return;

        var orderListId = filledOrder.Command.OrderListId.Value;
        if (!_orderListContingencies.TryGetValue(orderListId, out var contingency))
            return;

        var isFullyFilled = filledOrder.RemainingQuantity.Value <= 0m;
        if (contingency == ContingencyType.OCO && isFullyFilled)
            CancelOcoSiblings(filledOrderId, filledOrder);
        else if (contingency == ContingencyType.OTO
            && (isFullyFilled || !GetVenueSimulationPolicy(filledOrder.Command.Instrument.Venue).OtoFullTrigger))
            ActivateOtoChildren(filledOrderId, filledOrder);
        else if (contingency == ContingencyType.OUO)
            UpdateOuoSiblings(filledOrderId, filledOrder);
    }

    private void ActivateOtoChildren(OrderId filledOrderId, SimulatedOrder filledOrder)
    {
        var orderListId = filledOrder.Command.OrderListId!.Value;
        if (!_otoParentOrders.TryGetValue(orderListId, out var parentOrderId) || parentOrderId != filledOrderId)
            return;

        _triggeredOtoLists.Add(orderListId);
        if (!_stagedOtoChildren.Remove(orderListId, out var children))
            return;

        foreach (var child in children)
            ProcessSubmitOrder(child);
    }

    private void UpdateOuoSiblings(OrderId filledOrderId, SimulatedOrder filledOrder)
    {
        var orderListId = filledOrder.Command.OrderListId!.Value;
        if (!_ouoParentOrders.TryGetValue(orderListId, out var parentOrderId) || parentOrderId != filledOrderId)
            return;

        var filledQuantity = filledOrder.Command.Quantity - filledOrder.RemainingQuantity;
        if (filledQuantity.Value <= 0m)
            return;

        foreach (var (orderId, sibling) in _openOrders.ToArray())
        {
            if (orderId == filledOrderId
                || sibling.Command.OrderListId != orderListId
                || sibling.Command.StrategyId != filledOrder.Command.StrategyId
                || sibling.Command.VariantId != filledOrder.Command.VariantId)
            {
                continue;
            }

            ResizeOpenOrder(orderId, sibling, filledQuantity);
        }

        foreach (var pending in _inflightCommands.ToArray())
        {
            if (pending.Kind != InflightCommandKind.Submit)
                continue;

            var sibling = pending.SubmitCommand!.Value;
            if (sibling.OrderId == filledOrderId
                || sibling.OrderListId != orderListId
                || sibling.StrategyId != filledOrder.Command.StrategyId
                || sibling.VariantId != filledOrder.Command.VariantId)
            {
                continue;
            }

            _inflightCommands.Remove(pending);
            _inflightCommands.Add(pending with { SubmitCommand = sibling with { Quantity = filledQuantity } });
        }
    }

    private void ResizeOpenOrder(OrderId orderId, SimulatedOrder order, Qty newQuantity)
    {
        var filledQuantity = order.Command.Quantity - order.RemainingQuantity;
        order.Command = order.Command with { Quantity = newQuantity };
        order.RemainingQuantity = new Qty(Math.Max(0m, newQuantity.Value - filledQuantity.Value));
        RefreshDisplayQuantity(order);
        order.ReservedCash = EstimateCapitalRequirement(order.Command, order.RemainingQuantity);
        _restingBook.AddOrUpdate(orderId, order, losesPriority: false);

        EmitConnectorEvent(new OrderAccepted(
            orderId,
            order.Command.StrategyId,
            order.Command.VariantId));

        if (order.RemainingQuantity.Value <= 0m)
            _filledOrdersBuffer.Add(orderId);
    }

    private static void RefreshDisplayQuantity(SimulatedOrder order)
    {
        if (!order.Command.DisplayQuantity.HasValue)
            return;

        order.DisplayRemaining = order.RemainingQuantity.Value <= 0m
            ? Qty.Zero
            : new Qty(Math.Min(order.Command.DisplayQuantity.Value.Value, order.RemainingQuantity.Value));
    }

    private void CancelOcoSiblings(OrderId filledOrderId, SimulatedOrder filledOrder)
    {
        var orderListId = filledOrder.Command.OrderListId!.Value;
        foreach (var (orderId, sibling) in _openOrders.ToArray())
        {
            if (orderId == filledOrderId
                || sibling.Command.OrderListId != orderListId
                || sibling.Command.StrategyId != filledOrder.Command.StrategyId
                || sibling.Command.VariantId != filledOrder.Command.VariantId)
            {
                continue;
            }

            _filledOrdersBuffer.Add(orderId);
            EmitConnectorEvent(new OrderCancelled(
                orderId,
                sibling.Command.StrategyId,
                sibling.Command.VariantId,
                sibling.RemainingQuantity,
                $"Cancelled by OCO sibling {filledOrderId.Value} fill."));
        }

        foreach (var pending in _inflightCommands.ToArray())
        {
            if (pending.Kind != InflightCommandKind.Submit)
                continue;

            var sibling = pending.SubmitCommand!.Value;
            if (sibling.OrderId == filledOrderId
                || sibling.OrderListId != orderListId
                || sibling.StrategyId != filledOrder.Command.StrategyId
                || sibling.VariantId != filledOrder.Command.VariantId)
            {
                continue;
            }

            _inflightCommands.Remove(pending);
            EmitConnectorEvent(new OrderCancelled(
                sibling.OrderId,
                sibling.StrategyId,
                sibling.VariantId,
                sibling.Quantity,
                $"Cancelled by OCO sibling {filledOrderId.Value} fill before exchange arrival."));
        }
    }

    private bool TryFillImmediately(SimulatedOrder order, out Price fillPrice)
    {
        fillPrice = default;

        if (order.Command.Type != OrderType.Limit)
            return false;

        if (!order.Command.LimitPrice.HasValue)
            return false;

        var depth = _depths.GetValueOrDefault(order.Command.Instrument);
        if (depth == null)
            return false;

        var limitPriceTick = TickPrice.FromPrice(order.Command.LimitPrice.Value, depth.TickSize).Ticks;
        var ctx = new FillModelContext
        {
            OrderPriceTick = limitPriceTick,
            BestBidTick = depth.BestBidTick,
            BestAskTick = depth.BestAskTick,
            QueueRelativePosition = 0d,
            OrderQty = order.Command.Quantity,
            OrderSide = order.Command.Side,
            NominalFillPrice = order.Command.LimitPrice.Value,
            Depth = depth,
            Trade = null
        };

        if (!_fillModel.ShouldFillLimit(ref ctx))
            return false;

        fillPrice = _fillModel.AdjustFillPrice(ref ctx);
        return true;
    }

    private bool TryFillTriggeredOrder(
        SimulatedOrder order,
        FinanceEvent evt,
        out Price fillPrice,
        out bool isMaker)
    {
        isMaker = false;
        fillPrice = default;

        if (TryUpdateTrailingStop(order, evt, out var trailingStop))
            order.Command = order.Command with { StopPrice = trailingStop };

        if (order.Command.Type is OrderType.MarketToLimit)
            return TryFillMarketToLimit(order, evt, out fillPrice, out isMaker);

        if (order.Command.Type is OrderType.MarketIfTouched or OrderType.LimitIfTouched)
            return TryFillIfTouched(order, evt, out fillPrice, out isMaker);

        if (order.Command.Type is not (OrderType.StopMarket
            or OrderType.StopLimit
            or OrderType.TrailingStopMarket
            or OrderType.TrailingStopLimit))
        {
            return false;
        }

        var stopPrice = order.Command.StopPrice;
        if (!stopPrice.HasValue)
            return false;

        order.StopTriggered |= IsStopTriggered(order.Command.Side, stopPrice.Value, evt);
        if (!order.StopTriggered)
            return false;

        if (order.Command.Type is OrderType.StopMarket or OrderType.TrailingStopMarket)
        {
            fillPrice = stopPrice.Value;
            return true;
        }

        if (!order.Command.LimitPrice.HasValue)
            return false;

        isMaker = true;
        return IsLimitTouched(order.Command.Side, order.Command.LimitPrice.Value, evt, out fillPrice);
    }

    private static bool TryFillMarketToLimit(
        SimulatedOrder order,
        FinanceEvent evt,
        out Price fillPrice,
        out bool isMaker)
    {
        isMaker = false;
        if (TryGetMarketPrice(evt, order.Command.Side, out fillPrice))
            return true;

        if (!order.Command.LimitPrice.HasValue)
            return false;

        isMaker = true;
        return IsLimitTouched(order.Command.Side, order.Command.LimitPrice.Value, evt, out fillPrice);
    }

    private static bool TryFillIfTouched(
        SimulatedOrder order,
        FinanceEvent evt,
        out Price fillPrice,
        out bool isMaker)
    {
        isMaker = false;
        fillPrice = default;

        var trigger = order.Command.StopPrice;
        if (!trigger.HasValue || !IsTouchedTrigger(order.Command.Side, trigger.Value, evt))
            return false;

        if (order.Command.Type == OrderType.MarketIfTouched)
        {
            fillPrice = trigger.Value;
            return true;
        }

        if (!order.Command.LimitPrice.HasValue)
            return false;

        isMaker = true;
        return IsLimitTouched(order.Command.Side, order.Command.LimitPrice.Value, evt, out fillPrice);
    }

    private bool TryUpdateTrailingStop(SimulatedOrder order, FinanceEvent evt, out Price stopPrice)
    {
        stopPrice = default;
        if (order.Command.Type is not (OrderType.TrailingStopMarket or OrderType.TrailingStopLimit))
            return false;

        if (!TryGetTrailingReference(order.Command.Side, evt, out var reference))
            return false;

        order.TrailingReference = order.TrailingReference.HasValue
            ? order.Command.Side == Side.Buy
                ? Price.Min(order.TrailingReference.Value, reference)
                : Price.Max(order.TrailingReference.Value, reference)
            : reference;

        if (!TryCalculateTrailingOffset(order.Command, order.TrailingReference.Value, out var offset))
            return false;

        stopPrice = order.Command.Side == Side.Buy
            ? new Price(order.TrailingReference.Value.Value + offset, order.TrailingReference.Value.Currency)
            : new Price(order.TrailingReference.Value.Value - offset, order.TrailingReference.Value.Currency);
        return true;
    }

    private bool TryCalculateTrailingOffset(SubmitOrder command, Price reference, out decimal offset)
    {
        offset = default;
        if (!command.TrailingOffset.HasValue || !command.TrailingOffsetType.HasValue)
            return false;

        offset = command.TrailingOffsetType.Value switch
        {
            TrailingOffsetType.Price => command.TrailingOffset.Value,
            TrailingOffsetType.Percent => reference.Value * command.TrailingOffset.Value / 100m,
            TrailingOffsetType.Ticks when _depths.TryGetValue(command.Instrument, out var depth) =>
                command.TrailingOffset.Value * depth.TickSize,
            _ => 0m
        };

        return offset > 0m;
    }

    private static bool IsTouchedTrigger(Side side, Price triggerPrice, FinanceEvent evt)
    {
        return evt switch
        {
            BarClosed bar => side == Side.Buy
                ? bar.Bar.Low.Value <= triggerPrice.Value
                : bar.Bar.High.Value >= triggerPrice.Value,
            QuoteReceived quote => side == Side.Buy
                ? quote.Quote.Ask.Value <= triggerPrice.Value
                : quote.Quote.Bid.Value >= triggerPrice.Value,
            TradeOccurred trade => side == Side.Buy
                ? trade.Trade.Price.Value <= triggerPrice.Value
                : trade.Trade.Price.Value >= triggerPrice.Value,
            _ => false
        };
    }

    private static bool TryGetMarketPrice(FinanceEvent evt, Side side, out Price price)
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

    private static bool TryGetTrailingReference(Side side, FinanceEvent evt, out Price price)
    {
        switch (evt)
        {
            case BarClosed bar:
                price = side == Side.Buy ? bar.Bar.Low : bar.Bar.High;
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

    private static bool IsStopTriggered(Side side, Price stopPrice, FinanceEvent evt)
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

    private static bool IsLimitTouched(Side side, Price limitPrice, FinanceEvent evt, out Price fillPrice)
    {
        fillPrice = limitPrice;
        return evt switch
        {
            BarClosed bar => side == Side.Buy
                ? bar.Bar.Low.Value <= limitPrice.Value
                : bar.Bar.High.Value >= limitPrice.Value,
            QuoteReceived quote => side == Side.Buy
                ? quote.Quote.Ask.Value <= limitPrice.Value
                : quote.Quote.Bid.Value >= limitPrice.Value,
            TradeOccurred trade => side == Side.Buy
                ? trade.Trade.Price.Value <= limitPrice.Value
                : trade.Trade.Price.Value >= limitPrice.Value,
            _ => false
        };
    }

    private Qty DetermineFillQuantity(SimulatedOrder order, FinanceEvent evt, decimal remainingTradeQuantity)
    {
        var quantity = order.RemainingQuantity;
        if (_config.FillBehavior == FillBehavior.PartialFillOnTrade && evt is TradeOccurred trade)
        {
            quantity = order.RemainingQuantity.Value <= trade.Trade.Size.Value
                ? order.RemainingQuantity
                : trade.Trade.Size;
            if (remainingTradeQuantity < quantity.Value)
                quantity = new Qty(remainingTradeQuantity);
        }

        if (order.DisplayRemaining.HasValue && order.DisplayRemaining.Value.Value < quantity.Value)
            return order.DisplayRemaining.Value;

        return quantity;
    }

    private void EmitFill(OrderId orderId, SimulatedOrder order, Price fillPrice, Qty fillQuantity, bool isMaker)
    {
        if (_events == null) return;
        if (fillQuantity.Value <= 0m) return;

        var strategyId = _orderStrategyMap.GetValueOrDefault(orderId, order.Command.StrategyId);
        fillPrice = ApplyPriceImprovement(fillPrice, order.Command.Side, isMaker);
        var commission = CalculateCommission(strategyId, order.Command.VariantId, order.Command.Instrument, order.Command.Side, fillQuantity, fillPrice, isMaker);

        EmitConnectorEvent(new OrderFilled(
            orderId,
            order.Command.Instrument,
            order.Command.VariantId,
            strategyId,
            order.Command.Side,
            fillQuantity,
            fillPrice,
            commission));
        order.RemainingQuantity -= fillQuantity;
        TrackFeeNotional(strategyId, order.Command.VariantId, order.Command.Instrument, fillQuantity, fillPrice);
        EmitOrderFillState(
            orderId,
            strategyId,
            order.Command.VariantId,
            fillQuantity,
            order.RemainingQuantity);
        if (order.DisplayRemaining.HasValue)
        {
            order.DisplayRemaining = new Qty(Math.Max(0m, order.DisplayRemaining.Value.Value - fillQuantity.Value));
            if (order.DisplayRemaining.Value.Value <= 0m && order.RemainingQuantity.Value > 0m)
                RefreshDisplayQuantity(order);
        }

        ApplyCashFill(strategyId, order.Command.Instrument, order.Command.VariantId, order.Command.Side, fillQuantity, fillPrice, commission);
        if (order.ReservedCash.Amount > 0m)
        {
            var consumed = GetUpfrontCashFlow(GetContract(order.Command.Instrument), fillQuantity, fillPrice) + commission;
            order.ReservedCash = consumed.Amount >= order.ReservedCash.Amount
                ? Money.Zero(order.ReservedCash.Currency)
                : order.ReservedCash - consumed;
        }

        ApplyFill(
            strategyId,
            order.Command.Instrument,
            order.Command.VariantId,
            order.Command.Side,
            fillQuantity,
            fillPrice,
            commission);
    }

    private void ApplyCashFill(
        StrategyId strategyId,
        Instrument instrument,
        int variantId,
        Side side,
        Qty quantity,
        Price price,
        Money commission)
    {
        var cash = GetCashBalance(strategyId, variantId, price.Currency);
        var contract = GetContract(instrument);
        var notional = GetUpfrontCashFlow(contract, quantity, price);
        var realized = GetDerivativeRealizedCashFlow(strategyId, instrument, variantId, side, quantity, price);
        if (side == Side.Buy)
        {
            SetCashBalance(strategyId, variantId, cash - notional + realized - commission);
            return;
        }

        var proceeds = notional + realized - commission;
        if (_config.AccountType == AccountType.Cash && _config.Settlement.CashProceedsDelay > Duration.Zero)
        {
            var cashEventTime = GetCashEventTime();
            var settlementId = SettlementId.New();
            var settlesAt = _config.Settlement.GetSettlementTime(cashEventTime);
            _pendingSettlements.Add(new PendingSettlement(
                settlementId,
                strategyId,
                variantId,
                proceeds,
                settlesAt));
            EmitConnectorEvent(new SettlementScheduled(
                settlementId,
                strategyId,
                variantId,
                proceeds,
                settlesAt));
            EmitConnectorEvent(new SettlementStatusSnapshot(
                settlementId,
                strategyId,
                variantId,
                SettlementStatus.Scheduled,
                proceeds,
                settlesAt,
                cashEventTime));
            return;
        }

        SetCashBalance(strategyId, variantId, cash + proceeds);
    }

    private Instant GetCashEventTime()
        => _currentReplayTime == default ? Instant.Now : _currentReplayTime;

    private void ApplySettlements(Instant now)
    {
        for (var i = _pendingSettlements.Count - 1; i >= 0; i--)
        {
            var settlement = _pendingSettlements[i];
            if (settlement.SettlesAt > now)
                continue;

            var cash = GetCashBalance(settlement.StrategyId, settlement.VariantId, settlement.Amount.Currency);
            SetCashBalance(settlement.StrategyId, settlement.VariantId, cash + settlement.Amount);
            _pendingSettlements.RemoveAt(i);
            EmitConnectorEvent(new SettlementReleased(
                settlement.SettlementId,
                settlement.StrategyId,
                settlement.VariantId,
                settlement.Amount,
                now));
            EmitConnectorEvent(new SettlementStatusSnapshot(
                settlement.SettlementId,
                settlement.StrategyId,
                settlement.VariantId,
                SettlementStatus.Released,
                settlement.Amount,
                settlement.SettlesAt,
                now));
            EmitPerformanceSnapshot(settlement.StrategyId, settlement.VariantId, settlement.Amount.Currency);
            EmitAccountStatement(settlement.StrategyId, settlement.VariantId, settlement.Amount.Currency);
        }
    }

    private void EmitPendingSettlementStatuses()
    {
        if (_events == null)
            return;

        var now = GetCashEventTime();
        foreach (var settlement in _pendingSettlements)
        {
            EmitConnectorEvent(new SettlementStatusSnapshot(
                settlement.SettlementId,
                settlement.StrategyId,
                settlement.VariantId,
                SettlementStatus.Pending,
                settlement.Amount,
                settlement.SettlesAt,
                now));
        }
    }

    private void ProcessAssetDeliveries(Instant now)
    {
        for (var i = _pendingAssetDeliveries.Count - 1; i >= 0; i--)
        {
            var delivery = _pendingAssetDeliveries[i];
            if (delivery.DeliversAt > now)
                continue;

            AddSettledQuantity(delivery.StrategyId, delivery.Instrument, delivery.VariantId, delivery.Quantity);
            _pendingAssetDeliveries.RemoveAt(i);
            EmitConnectorEvent(new AssetDelivered(
                delivery.DeliveryId,
                delivery.StrategyId,
                delivery.VariantId,
                delivery.Instrument,
                delivery.Quantity,
                now));
            EmitConnectorEvent(new AssetDeliveryStatusSnapshot(
                delivery.DeliveryId,
                delivery.StrategyId,
                delivery.VariantId,
                delivery.Instrument,
                delivery.Quantity,
                AssetDeliveryStatus.Delivered,
                delivery.DeliversAt,
                now));

            if (_positions.TryGetValue((delivery.StrategyId, delivery.Instrument, delivery.VariantId), out var position))
            {
                var currency = position.AvgEntryPrice.Currency == default
                    ? _initialCash.Currency
                    : position.AvgEntryPrice.Currency;
                EmitCustodyPositionSnapshot(
                    delivery.StrategyId,
                    delivery.Instrument,
                    delivery.VariantId,
                    position,
                    currency,
                    position.AvgEntryPrice);
            }
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
        var advancement = _config.QueueModel.CalculateAdvancement(
            currentPosition,
            trade.Trade.Size.Value);

        return Math.Max(0m, currentPosition - advancement);
    }

    private decimal GetPositionQuantity(SubmitOrder command)
        => _positions.TryGetValue((command.StrategyId, command.Instrument, command.VariantId), out var position)
            ? position.Quantity.Value
            : 0m;

    private static bool IsExpired(SimulatedOrder order, FinanceEvent evt)
    {
        var command = order.Command;
        var eventTime = GetEventTime(evt);

        if (command.TimeInForce == TimeInForce.GTD)
            return command.GoodTilDate.HasValue && eventTime >= command.GoodTilDate.Value;

        return command.TimeInForce == TimeInForce.Day
            && eventTime.ToDateTimeOffset().UtcDateTime.Date > order.SubmitTime.ToDateTimeOffset().UtcDateTime.Date;
    }

    private static Instant GetEventTime(FinanceEvent evt)
    {
        return evt switch
        {
            QuoteReceived quote => quote.Quote.Time.ExchangeTime,
            TradeOccurred trade => trade.Trade.Time.ExchangeTime,
            BarClosed bar => bar.Bar.Time,
            BookSnapshotReceived book => book.Book.Time,
            BookLevelDeltaReceived delta => delta.Time,
            BookLevelDeltasReceived deltas => deltas.Time,
            BookDepthSnapshotReceived depth => depth.Time,
            BookDepth10Received depth => depth.Time,
            _ => evt.Time
        };
    }

    private void ApplyFill(
        StrategyId strategyId,
        Instrument instrument,
        int variantId,
        Side side,
        Qty quantity,
        Price price,
        Money commission)
    {
        var key = (strategyId, instrument, variantId);
        if (!_positions.TryGetValue(key, out var position))
        {
            position = Position.Empty(instrument);
            _positions[key] = position;
        }

        position.ApplyFill(GetContract(instrument), side, quantity, price, commission);
        ApplyAssetDeliveryFill(strategyId, instrument, variantId, side, quantity);
        EmitCustodyPositionSnapshot(strategyId, instrument, variantId, position, price.Currency, price);
        EmitPerformanceSnapshot(strategyId, variantId, price.Currency);
        EmitAccountStatement(strategyId, variantId, price.Currency);
    }

    private void ApplyAssetDeliveryFill(
        StrategyId strategyId,
        Instrument instrument,
        int variantId,
        Side side,
        Qty quantity)
    {
        if (_config.AccountType == AccountType.Cash
            && _config.Settlement.CashProceedsDelay > Duration.Zero
            && side == Side.Buy)
        {
            ScheduleAssetDelivery(strategyId, instrument, variantId, quantity);
            return;
        }

        if (side == Side.Buy)
        {
            AddSettledQuantity(strategyId, instrument, variantId, quantity);
            return;
        }

        var remainingSettledQuantity = ConsumePendingAssetDeliveries(strategyId, instrument, variantId, quantity);
        if (remainingSettledQuantity.Value > 0m)
            SubtractSettledQuantity(strategyId, instrument, variantId, remainingSettledQuantity);
    }

    private void ScheduleAssetDelivery(
        StrategyId strategyId,
        Instrument instrument,
        int variantId,
        Qty quantity)
    {
        var now = GetCashEventTime();
        var deliveryId = AssetDeliveryId.New();
        var deliversAt = _config.Settlement.GetSettlementTime(now);
        var delivery = new PendingAssetDelivery(
            deliveryId,
            strategyId,
            instrument,
            variantId,
            quantity,
            deliversAt);

        _pendingAssetDeliveries.Add(delivery);
        EmitConnectorEvent(new AssetDeliveryScheduled(
            deliveryId,
            strategyId,
            variantId,
            instrument,
            quantity,
            deliversAt));
        EmitConnectorEvent(new AssetDeliveryStatusSnapshot(
            deliveryId,
            strategyId,
            variantId,
            instrument,
            quantity,
            AssetDeliveryStatus.Scheduled,
            deliversAt,
            now));
    }

    private Qty ConsumePendingAssetDeliveries(
        StrategyId strategyId,
        Instrument instrument,
        int variantId,
        Qty quantity)
    {
        var remaining = quantity.Value;
        for (var i = 0; i < _pendingAssetDeliveries.Count && remaining > 0m; i++)
        {
            var delivery = _pendingAssetDeliveries[i];
            if (delivery.StrategyId != strategyId
                || delivery.Instrument != instrument
                || delivery.VariantId != variantId)
            {
                continue;
            }

            var canceled = Math.Min(remaining, delivery.Quantity.Value);
            remaining -= canceled;
            var canceledQty = new Qty(canceled);
            var now = GetCashEventTime();
            EmitConnectorEvent(new AssetDeliveryCanceled(
                delivery.DeliveryId,
                strategyId,
                variantId,
                instrument,
                canceledQty,
                now));
            EmitConnectorEvent(new AssetDeliveryStatusSnapshot(
                delivery.DeliveryId,
                strategyId,
                variantId,
                instrument,
                canceledQty,
                AssetDeliveryStatus.Canceled,
                delivery.DeliversAt,
                now));

            var remainingDeliveryQty = delivery.Quantity.Value - canceled;
            if (remainingDeliveryQty <= 0m)
            {
                _pendingAssetDeliveries.RemoveAt(i);
                i--;
            }
            else
            {
                _pendingAssetDeliveries[i] = delivery with { Quantity = new Qty(remainingDeliveryQty) };
            }
        }

        return new Qty(remaining);
    }

    private void AddSettledQuantity(StrategyId strategyId, Instrument instrument, int variantId, Qty quantity)
    {
        var key = (strategyId, instrument, variantId);
        var current = _settledPositions.GetValueOrDefault(key);
        _settledPositions[key] = new Qty(current.Value + quantity.Value);
    }

    private void SubtractSettledQuantity(StrategyId strategyId, Instrument instrument, int variantId, Qty quantity)
    {
        var key = (strategyId, instrument, variantId);
        var current = _settledPositions.GetValueOrDefault(key);
        _settledPositions[key] = new Qty(current.Value - quantity.Value);
    }

    private Qty GetSettledQuantity(StrategyId strategyId, Instrument instrument, int variantId)
        => _settledPositions.GetValueOrDefault((strategyId, instrument, variantId));

    private Qty GetPendingDeliveryQuantity(StrategyId strategyId, Instrument instrument, int variantId)
    {
        var quantity = 0m;
        foreach (var delivery in _pendingAssetDeliveries)
        {
            if (delivery.StrategyId == strategyId
                && delivery.Instrument == instrument
                && delivery.VariantId == variantId)
            {
                quantity += delivery.Quantity.Value;
            }
        }

        return new Qty(quantity);
    }

    private Qty GetRehypothecatableQuantity(StrategyId strategyId, Instrument instrument, int variantId)
    {
        if (_config.AccountType != AccountType.Margin
            || _config.Margin.RehypothecationPolicy != RehypothecationPolicy.Allowed)
        {
            return Qty.Zero;
        }

        var settled = GetSettledQuantity(strategyId, instrument, variantId);
        if (settled.Value <= 0m)
            return Qty.Zero;

        return _config.Margin.RehypothecationAvailability.TryGetValue(instrument, out var available)
            ? new Qty(Math.Min(settled.Value, available.Value))
            : settled;
    }

    private void EmitCustodyPositionSnapshots()
    {
        if (_events == null)
            return;

        foreach (var ((strategyId, instrument, variantId), position) in _positions)
        {
            var currency = position.AvgEntryPrice.Currency == default
                ? _initialCash.Currency
                : position.AvgEntryPrice.Currency;
            EmitCustodyPositionSnapshot(strategyId, instrument, variantId, position, currency, position.AvgEntryPrice);
        }
    }

    private void EmitCustodyPositionSnapshot(
        StrategyId strategyId,
        Instrument instrument,
        int variantId,
        Position position,
        Currency currency,
        Price fallbackMark)
    {
        if (_events == null)
            return;

        var mark = TryGetMarkPrice(instrument, position.Side, currency, out var markPrice)
            ? markPrice
            : fallbackMark;
        if (mark.Currency == default)
            mark = new Price(mark.Value, currency);

        var value = position.IsFlat
            ? new PositionValuation(Money.Zero(currency), Money.Zero(currency), Money.Zero(currency), Money.Zero(currency))
            : ValuePosition(instrument, position, mark);

        EmitConnectorEvent(new CustodyPositionSnapshot(
            strategyId,
            variantId,
            instrument,
            position.Quantity,
            GetSettledQuantity(strategyId, instrument, variantId),
            GetPendingDeliveryQuantity(strategyId, instrument, variantId),
            GetRehypothecatableQuantity(strategyId, instrument, variantId),
            position.AvgEntryPrice,
            mark,
            value.MarketValue,
            value.UnrealizedPnL,
            position.RealizedPnL.Currency == default ? Money.Zero(currency) : position.RealizedPnL,
            IsOpen: !position.IsFlat)
        {
            Time = GetCashEventTime()
        });
    }

    private void EmitPerformanceSnapshot(StrategyId strategyId, int variantId, Currency currency)
    {
        if (_events == null)
            return;

        var cash = GetCashBalance(strategyId, variantId, currency);
        var pendingSettlement = GetPendingSettlementTotal(strategyId, variantId, currency);
        var marketValue = Money.Zero(currency);
        var equityContribution = Money.Zero(currency);
        var realizedPnL = Money.Zero(currency);
        var unrealizedPnL = Money.Zero(currency);
        var openPositions = 0;

        foreach (var ((positionStrategyId, instrument, positionVariantId), position) in _positions)
        {
            if (positionStrategyId != strategyId || positionVariantId != variantId || position.IsFlat)
                continue;

            openPositions++;
            realizedPnL += position.RealizedPnL;

            var mark = TryGetMarkPrice(instrument, position.Side, currency, out var markPrice)
                ? markPrice
                : position.AvgEntryPrice;
            var value = ValuePosition(instrument, position, mark);
            if (value.MarketValue.Currency == currency)
                marketValue += value.MarketValue;
            if (value.UnrealizedPnL.Currency == currency)
                unrealizedPnL += value.UnrealizedPnL;
            var contribution = GetEquityContribution(instrument, value);
            if (contribution.Currency == currency)
                equityContribution += contribution;
        }

        foreach (var ((positionStrategyId, _, positionVariantId), position) in _positions)
        {
            if (positionStrategyId == strategyId && positionVariantId == variantId && position.IsFlat)
                realizedPnL += position.RealizedPnL;
        }

        var openOrders = _openOrders.Values.Count(order =>
            order.Command.StrategyId == strategyId && order.Command.VariantId == variantId);
        EmitConnectorEvent(new PerformanceSnapshot(
            Equity: cash + pendingSettlement + equityContribution,
            Cash: cash,
            UnrealizedPnL: unrealizedPnL,
            RealizedPnL: realizedPnL,
            OpenPositions: openPositions,
            OpenOrders: openOrders));
    }

    private void EmitAccountStatements()
    {
        if (_events == null)
            return;

        foreach (var (strategyId, variantId, currency) in GetAccountKeys())
            EmitAccountStatement(strategyId, variantId, currency);
    }

    private void EmitAccountStatement(StrategyId strategyId, int variantId, Currency currency)
    {
        if (_events == null)
            return;

        var statement = CreateAccountStatement(strategyId, variantId, currency);
        EmitConnectorEvent(statement);
    }

    private IEnumerable<(StrategyId StrategyId, int VariantId, Currency Currency)> GetAccountKeys()
    {
        var keys = new HashSet<(StrategyId StrategyId, int VariantId, Currency Currency)>();

        foreach (var (strategyId, variantId, currency) in _cashBalances.Keys)
            keys.Add((strategyId, variantId, currency));

        foreach (var settlement in _pendingSettlements)
            keys.Add((settlement.StrategyId, settlement.VariantId, settlement.Amount.Currency));

        foreach (var open in _openOrders.Values)
        {
            var currency = open.ReservedCash.Currency == default
                ? _initialCash.Currency
                : open.ReservedCash.Currency;
            keys.Add((open.Command.StrategyId, open.Command.VariantId, currency));
        }

        foreach (var ((strategyId, _, variantId), position) in _positions)
        {
            keys.Add((strategyId, variantId, position.AvgEntryPrice.Currency));
            if (position.RealizedPnL.Currency != default)
                keys.Add((strategyId, variantId, position.RealizedPnL.Currency));
        }

        return keys;
    }

    private AccountStatementSnapshot CreateAccountStatement(StrategyId strategyId, int variantId, Currency currency)
    {
        var cash = GetCashBalance(strategyId, variantId, currency);
        var pendingSettlement = GetPendingSettlementTotal(strategyId, variantId, currency);
        var reservedCash = GetReservedCashTotal(strategyId, variantId, currency);
        var marketValue = Money.Zero(currency);
        var equityContribution = Money.Zero(currency);
        var realizedPnL = Money.Zero(currency);
        var unrealizedPnL = Money.Zero(currency);
        var openPositions = 0;

        foreach (var ((positionStrategyId, instrument, positionVariantId), position) in _positions)
        {
            if (positionStrategyId != strategyId || positionVariantId != variantId)
                continue;

            if (position.IsFlat)
            {
                if (position.RealizedPnL.Currency == currency)
                    realizedPnL += position.RealizedPnL;
                continue;
            }

            openPositions++;
            if (position.RealizedPnL.Currency == currency)
                realizedPnL += position.RealizedPnL;

            var mark = TryGetMarkPrice(instrument, position.Side, currency, out var markPrice)
                ? markPrice
                : position.AvgEntryPrice;

            if (mark.Currency != currency)
                continue;

            var value = ValuePosition(instrument, position, mark);
            if (value.MarketValue.Currency == currency)
                marketValue += value.MarketValue;
            if (value.UnrealizedPnL.Currency == currency)
                unrealizedPnL += value.UnrealizedPnL;
            var contribution = GetEquityContribution(instrument, value);
            if (contribution.Currency == currency)
                equityContribution += contribution;
        }

        var openOrders = _openOrders.Values.Count(order =>
            order.Command.StrategyId == strategyId && order.Command.VariantId == variantId);

        return new AccountStatementSnapshot(
            strategyId,
            variantId,
            currency,
            Cash: cash,
            AvailableCash: cash - reservedCash,
            PendingSettlement: pendingSettlement,
            ReservedCash: reservedCash,
            MarketValue: marketValue,
            Equity: cash + pendingSettlement + equityContribution,
            UnrealizedPnL: unrealizedPnL,
            RealizedPnL: realizedPnL,
            OpenPositions: openPositions,
            OpenOrders: openOrders)
        {
            Time = GetCashEventTime()
        };
    }

    private void EmitMarginStatusSnapshots()
    {
        if (_events == null || _config.AccountType != AccountType.Margin)
            return;

        var emitted = new HashSet<(StrategyId StrategyId, int VariantId, Currency Currency)>();
        foreach (var ((strategyId, _, variantId), position) in _positions)
        {
            if (position.IsFlat)
                continue;

            var currency = position.AvgEntryPrice.Currency == default
                ? _initialCash.Currency
                : position.AvgEntryPrice.Currency;
            var key = (strategyId, variantId, currency);
            if (!emitted.Add(key))
                continue;

            var (equity, requirement) = CalculateMarginStatus(strategyId, variantId, currency);
            if (requirement.IsZero)
                continue;

            EmitConnectorEvent(new MarginStatusSnapshot(
                strategyId,
                variantId,
                equity,
                requirement,
                IsMaintenanceBreached: equity.Amount < requirement.Amount));
            ProcessMarginStatus(key, equity, requirement);
        }

        foreach (var key in _activeMarginCalls.Keys.Where(key => !emitted.Contains(key)).ToArray())
        {
            var (equity, requirement) = CalculateMarginStatus(key.StrategyId, key.VariantId, key.Currency);
            ResolveMarginCall(key, equity, requirement);
        }
    }

    private void ProcessMarginStatus(
        (StrategyId StrategyId, int VariantId, Currency Currency) key,
        Money equity,
        Money requirement)
    {
        if (equity.Amount >= requirement.Amount)
        {
            if (_activeMarginCalls.ContainsKey(key))
                ResolveMarginCall(key, equity, requirement);
            return;
        }

        var now = GetCashEventTime();
        if (!_activeMarginCalls.TryGetValue(key, out var call))
        {
            call = new ActiveMarginCall(
                equity,
                requirement,
                now + _config.Margin.MarginCallGracePeriod);
            _activeMarginCalls[key] = call;
            EmitConnectorEvent(new MarginCallIssued(
                key.StrategyId,
                key.VariantId,
                equity,
                requirement,
                call.DueAt));
        }

        if (now >= call.DueAt)
            LiquidateMarginBreach(key.StrategyId, key.VariantId, key.Currency);
    }

    private void ResolveMarginCall(
        (StrategyId StrategyId, int VariantId, Currency Currency) key,
        Money equity,
        Money requirement)
    {
        _activeMarginCalls.Remove(key);
        EmitConnectorEvent(new MarginCallResolved(
            key.StrategyId,
            key.VariantId,
            equity,
            requirement));
    }

    private (Money Equity, Money MaintenanceRequirement) CalculateMarginStatus(
        StrategyId strategyId,
        int variantId,
        Currency currency)
    {
        var cash = GetCashBalance(strategyId, variantId, currency);
        var equityContribution = Money.Zero(currency);
        var maintenance = Money.Zero(currency);

        foreach (var ((positionStrategyId, instrument, positionVariantId), position) in _positions)
        {
            if (positionStrategyId != strategyId || positionVariantId != variantId || position.IsFlat)
                continue;

            var mark = TryGetMarkPrice(instrument, position.Side, currency, out var markPrice)
                ? markPrice
                : position.AvgEntryPrice;
            var contract = GetContract(instrument);
            var value = ValuePosition(instrument, position, mark);
            var contribution = GetEquityContribution(instrument, value);
            var requirement = GetMaintenanceMarginRequirement(contract, position.Quantity, mark, null);
            if (contribution.Currency == currency)
                equityContribution += contribution;
            if (requirement.Currency == currency)
                maintenance += requirement;
        }

        return (cash + equityContribution, maintenance);
    }

    private void LiquidateMarginBreach(StrategyId strategyId, int variantId, Currency currency)
    {
        if (_events == null)
            return;

        _activeMarginCalls.Remove((strategyId, variantId, currency));

        EmitConnectorEvent(new RiskLimitBreached(
            $"MaintenanceMargin:{strategyId.Value}:{variantId}",
            CurrentValue: CalculateMarginStatus(strategyId, variantId, currency).Equity.Amount,
            LimitValue: CalculateMarginStatus(strategyId, variantId, currency).MaintenanceRequirement.Amount));

        CancelOpenOrdersForMarginBreach(strategyId, variantId);
        if (_config.Margin.LiquidationPolicy == LiquidationPolicy.CancelOpenOrdersOnly)
            return;

        var positions = _positions
            .Where(entry => entry.Key.StrategyId == strategyId
                && entry.Key.VariantId == variantId
                && !entry.Value.IsFlat)
            .OrderByDescending(entry => GetPositionMaintenanceRequirement(entry.Key.Instrument, entry.Value, currency).Amount)
            .ToArray();

        foreach (var ((_, instrument, _), position) in positions)
        {
            if (!TryGetMarkPrice(instrument, position.Side, currency, out var mark))
                mark = position.AvgEntryPrice;

            var quantity = position.Quantity.Abs;
            if (_config.Margin.LiquidationPolicy == LiquidationPolicy.CancelOpenOrdersAndReduceToMaintenance)
            {
                quantity = GetRequiredMaintenanceLiquidationQuantity(strategyId, variantId, instrument, currency, mark, quantity);
                if (quantity.Value <= 0m)
                    break;
            }

            var liquidationSide = position.IsLong ? Side.Sell : Side.Buy;
            var commission = CalculateCommission(strategyId, variantId, instrument, liquidationSide, quantity, mark, isMaker: false);
            var orderId = OrderId.New();

            EmitConnectorEvent(new OrderFilled(
                orderId,
                instrument,
                variantId,
                strategyId,
                liquidationSide,
                quantity,
                mark,
                commission));
            EmitOrderFillState(orderId, strategyId, variantId, quantity, Qty.Zero);
            TrackFeeNotional(strategyId, variantId, instrument, quantity, mark);
            ApplyCashFill(strategyId, instrument, variantId, liquidationSide, quantity, mark, commission);
            ApplyFill(strategyId, instrument, variantId, liquidationSide, quantity, mark, commission);
        }
    }

    private Money GetPositionMaintenanceRequirement(Instrument instrument, Position position, Currency currency)
    {
        if (position.IsFlat)
            return Money.Zero(currency);

        var mark = TryGetMarkPrice(instrument, position.Side, currency, out var markPrice)
            ? markPrice
            : position.AvgEntryPrice;
        var contract = GetContract(instrument);
        var requirement = GetMaintenanceMarginRequirement(contract, position.Quantity, mark, null);
        return requirement.Currency == default
            ? new Money(requirement.Amount, currency)
            : requirement;
    }

    private Qty GetRequiredMaintenanceLiquidationQuantity(
        StrategyId strategyId,
        int variantId,
        Instrument instrument,
        Currency currency,
        Price mark,
        Qty availableQuantity)
    {
        var (equity, requirement) = CalculateMarginStatus(strategyId, variantId, currency);
        var deficit = requirement.Amount - equity.Amount;
        if (deficit <= 0m)
            return Qty.Zero;

        var contract = GetContract(instrument);
        if (availableQuantity.Value <= 0m)
            return Qty.Zero;

        var positionRequirement = GetMaintenanceMarginRequirement(contract, availableQuantity, mark, null).Amount;
        var maintenancePerUnit = positionRequirement / availableQuantity.Value;
        if (maintenancePerUnit <= 0m)
            return availableQuantity;

        var quantity = deficit / maintenancePerUnit;
        if (quantity <= 0m)
            return Qty.Zero;

        return new Qty(Math.Min(availableQuantity.Value, quantity));
    }

    private InstrumentContract GetContract(Instrument instrument) =>
        InstrumentContracts.TryGetValue(instrument, out var contract)
            ? contract
            : Contracts.FromIdentity(instrument, _initialCash.Currency);

    private Money GetInitialMarginRequirement(
        InstrumentContract contract,
        Qty signedQuantity,
        Price mark,
        Price? underlyingMark)
    {
        if (contract.Payoff is PayoffTerms.Option option)
            return DefaultOptionMarginModel.Instance.InitialMargin(
                BuildOptionMarginRequest(contract, option.Terms, signedQuantity, mark, underlyingMark)).Requirement;

        var (initial, _) = GetMarginFractions(contract);
        return _valuation.Notional(contract, signedQuantity, mark) * initial;
    }

    private Money GetMaintenanceMarginRequirement(
        InstrumentContract contract,
        Qty signedQuantity,
        Price mark,
        Price? underlyingMark)
    {
        if (contract.Payoff is PayoffTerms.Option option)
            return DefaultOptionMarginModel.Instance.MaintenanceMargin(
                BuildOptionMarginRequest(contract, option.Terms, signedQuantity, mark, underlyingMark)).Requirement;

        var (_, maintenance) = GetMarginFractions(contract);
        return _valuation.Notional(contract, signedQuantity, mark) * maintenance;
    }

    private static OptionMarginRequest BuildOptionMarginRequest(
        InstrumentContract contract,
        OptionTerms terms,
        Qty signedQuantity,
        Price optionMark,
        Price? underlyingMark) =>
        new(
            contract,
            signedQuantity,
            new OptionMarketState(
                contract.Instrument,
                Timestamp: Instant.MinValue,
                Last: optionMark,
                UnderlyingMark: underlyingMark ?? terms.Strike.ScaledStrike),
            new OptionPricingScenario(RiskFreeRate: 0m));

    private (decimal Initial, decimal Maintenance) GetMarginFractions(InstrumentContract contract)
        => contract.Margin switch
        {
            MarginTerms.CashMargin => (1m, 1m),
            MarginTerms.RegT => (_config.Margin.InitialMarginFraction, _config.Margin.MaintenanceMarginFraction),
            MarginTerms.FixedFraction fixedFraction => (fixedFraction.Initial, fixedFraction.Maintenance),
            MarginTerms.Portfolio => (_config.Margin.InitialMarginFraction, _config.Margin.MaintenanceMarginFraction),
            _ => (_config.Margin.InitialMarginFraction, _config.Margin.MaintenanceMarginFraction)
        };

    private Money GetUpfrontCashFlow(InstrumentContract contract, Qty quantity, Price price)
    {
        if (contract.Payoff is PayoffTerms.Option option)
            return option.Terms.PremiumStyle switch
            {
                OptionPremiumStyle.Upfront => _valuation.MarketValue(contract, quantity, price),
                OptionPremiumStyle.FuturesStyle or OptionPremiumStyle.Deferred => Money.Zero(contract.Exposure.SettlementCurrency()),
                _ => throw new InvalidOperationException($"Unsupported option premium style {option.Terms.PremiumStyle}.")
            };

        if (contract.Payoff is PayoffTerms.Betting)
            return new Money(quantity.Abs.Value, contract.Exposure.SettlementCurrency());

        var shouldExchangeCash = contract.Exposure is EconomicExposure.Spot
            || contract.Payoff is PayoffTerms.Binary;
        return shouldExchangeCash
            ? _valuation.MarketValue(contract, quantity, price)
            : Money.Zero(contract.Exposure.SettlementCurrency());
    }

    private static Qty ToSignedQuantity(Side side, Qty quantity) =>
        side == Side.Sell ? -quantity.Abs : quantity.Abs;

    private Money GetDerivativeRealizedCashFlow(
        StrategyId strategyId,
        Instrument instrument,
        int variantId,
        Side side,
        Qty quantity,
        Price price)
    {
        var contract = GetContract(instrument);
        if (!GetUpfrontCashFlow(contract, quantity, price).IsZero)
            return Money.Zero(price.Currency);

        if (!_positions.TryGetValue((strategyId, instrument, variantId), out var position) || position.IsFlat)
            return Money.Zero(contract.Exposure.SettlementCurrency());

        var deltaSign = side == Side.Buy ? 1m : -1m;
        if (Math.Sign(position.Quantity.Value) == Math.Sign(deltaSign))
            return Money.Zero(contract.Exposure.SettlementCurrency());

        var closingQty = Math.Min(quantity.Value, position.Quantity.Abs.Value);
        return _valuation.RealizedPnL(
            contract,
            new Qty(closingQty * (position.Quantity.IsPositive ? 1m : -1m)),
            position.AvgEntryPrice,
            price);
    }

    private PositionValuation ValuePosition(Instrument instrument, Position position, Price mark)
    {
        var contract = GetContract(instrument);
        return _valuation.ValuePosition(
            contract,
            position.ToValuationInput(),
            mark);
    }

    private Money GetEquityContribution(Instrument instrument, PositionValuation value)
    {
        var contract = GetContract(instrument);
        var contributesMarketValue = contract.Exposure is EconomicExposure.Spot
            || contract.Payoff is PayoffTerms.Option or PayoffTerms.Binary;
        return contributesMarketValue ? value.MarketValue : value.UnrealizedPnL;
    }

    private void CancelOpenOrdersForMarginBreach(StrategyId strategyId, int variantId)
    {
        if (_events == null)
            return;

        foreach (var (orderId, order) in _openOrders.ToArray())
        {
            if (order.Command.StrategyId != strategyId || order.Command.VariantId != variantId)
                continue;

            RemoveOpenOrder(orderId);
            EmitConnectorEvent(new OrderCancelled(
                orderId,
                strategyId,
                variantId,
                order.RemainingQuantity,
                "Cancelled by margin liquidation."));
        }
    }

    private bool TryGetMarkPrice(Instrument instrument, Side side, Currency currency, out Price price)
    {
        if (_depths.TryGetValue(instrument, out var depth))
        {
            var tick = side == Side.Sell
                ? depth.BestAskTick
                : depth.BestBidTick;
            if (tick.HasValue)
            {
                price = new Price(tick.Value * depth.TickSize, currency);
                return true;
            }
        }

        if (_closingMarks.TryGetValue(instrument, out var closeMark))
        {
            price = closeMark.Currency == default
                ? new Price(closeMark.Value, currency)
                : closeMark;
            return true;
        }

        price = default;
        return false;
    }

    public void Dispose()
    {
        _openOrders.Clear();
        _orderStrategyMap.Clear();
        _orderListContingencies.Clear();
        _otoParentOrders.Clear();
        _ouoParentOrders.Clear();
        _stagedOtoChildren.Clear();
        _triggeredOtoLists.Clear();
        _venueStatuses.Clear();
        _instrumentStatuses.Clear();
        _closingMarks.Clear();
        _filledOrdersBuffer.Clear();
        _positions.Clear();
        _cashBalances.Clear();
        _pendingSettlements.Clear();
        _inflightCommands.Clear();
        _pendingResponseEvents.Clear();
        _activeAlgoOrders.Clear();
        _feeNotionalHistory.Clear();
        _activeMarginCalls.Clear();
        _restingBook.Clear();
        _depths.Clear();
        _nextVenueSequence = 0;
        _nextInflightSequence = 0;
        _isConnected = false;
    }

    private enum InflightCommandKind
    {
        Cancel = 0,
        Modify = 1,
        Submit = 2
    }

    private readonly record struct InflightCommand(
        Instant ArrivesAt,
        InflightCommandKind Kind,
        long Sequence,
        SubmitOrder? SubmitCommand = null,
        ModifyOrder? ModifyCommand = null,
        CancelOrder? CancelCommand = null)
    {
        public static InflightCommand Submit(SubmitOrder command, Instant arrivesAt, long sequence) =>
            new(arrivesAt, InflightCommandKind.Submit, sequence, SubmitCommand: command);

        public static InflightCommand Modify(ModifyOrder command, Instant arrivesAt, long sequence) =>
            new(arrivesAt, InflightCommandKind.Modify, sequence, ModifyCommand: command);

        public static InflightCommand Cancel(CancelOrder command, Instant arrivesAt, long sequence) =>
            new(arrivesAt, InflightCommandKind.Cancel, sequence, CancelCommand: command);
    }

    private sealed class InflightCommandComparer : IComparer<InflightCommand>
    {
        public static InflightCommandComparer Instance { get; } = new();

        private InflightCommandComparer()
        {
        }

        public int Compare(InflightCommand x, InflightCommand y)
        {
            var arrivesAt = x.ArrivesAt.CompareTo(y.ArrivesAt);
            if (arrivesAt != 0)
                return arrivesAt;

            var kind = x.Kind.CompareTo(y.Kind);
            if (kind != 0)
                return kind;

            return x.Sequence.CompareTo(y.Sequence);
        }
    }

    private readonly record struct PendingSettlement(
        SettlementId SettlementId,
        StrategyId StrategyId,
        int VariantId,
        Money Amount,
        Instant SettlesAt);

    private readonly record struct PendingAssetDelivery(
        AssetDeliveryId DeliveryId,
        StrategyId StrategyId,
        Instrument Instrument,
        int VariantId,
        Qty Quantity,
        Instant DeliversAt);

    private readonly record struct PendingResponseEvent(
        FinanceEvent Event,
        Instant VisibleAt);

    private readonly record struct ActiveMarginCall(
        Money InitialEquity,
        Money InitialMaintenanceRequirement,
        Instant DueAt);

    private readonly record struct AccountTradeNotional(
        StrategyId StrategyId,
        int VariantId,
        Money Notional,
        Instant TradedAt);

    private sealed class ReplayOrderBook
    {
        private readonly Dictionary<ReplayBookLevel, LinkedList<OrderId>> _levels = [];
        private readonly Dictionary<OrderId, ReplayBookLevel> _orders = [];

        public void AddOrUpdate(OrderId orderId, SimulatedOrder order, bool losesPriority)
        {
            if (!TryCreateLevel(order, out var level))
            {
                Remove(orderId);
                return;
            }

            if (_orders.TryGetValue(orderId, out var existingLevel)
                && existingLevel == level
                && !losesPriority)
            {
                return;
            }

            Remove(orderId);
            if (!_levels.TryGetValue(level, out var queue))
            {
                queue = new LinkedList<OrderId>();
                _levels[level] = queue;
            }

            queue.AddLast(orderId);
            _orders[orderId] = level;
        }

        public void Remove(OrderId orderId)
        {
            if (!_orders.Remove(orderId, out var level))
                return;

            if (!_levels.TryGetValue(level, out var queue))
                return;

            for (var node = queue.First; node != null; node = node.Next)
            {
                if (node.Value != orderId)
                    continue;

                queue.Remove(node);
                break;
            }

            if (queue.Count == 0)
                _levels.Remove(level);
        }

        public IEnumerable<OrderId> GetPassiveTradeOrderIds(
            TradeOccurred tradeEvent,
            IReadOnlyDictionary<OrderId, SimulatedOrder> openOrders)
        {
            var trade = tradeEvent.Trade;
            var passiveSide = trade.AggressorSide switch
            {
                Side.Sell => Side.Buy,
                Side.Buy => Side.Sell,
                _ => (Side?)null
            };

            if (!passiveSide.HasValue)
                yield break;

            var levels = _levels.Keys
                .Where(level => level.Instrument == tradeEvent.Instrument
                    && level.Side == passiveSide.Value
                    && IsPassivePrice(level, trade))
                .OrderBy(level => level.Side == Side.Buy ? -level.Price.Value : level.Price.Value)
                .ToArray();

            foreach (var level in levels)
            {
                foreach (var orderId in _levels[level])
                {
                    if (openOrders.TryGetValue(orderId, out var order)
                        && IsPassiveTradeCandidate(order.Command, trade))
                    {
                        yield return orderId;
                    }
                }
            }
        }

        public bool WouldCross(
            SubmitOrder incoming,
            IReadOnlyDictionary<OrderId, SimulatedOrder> openOrders)
        {
            if (!incoming.LimitPrice.HasValue)
                return false;

            return GetCrossingOrderIds(incoming, openOrders).Any();
        }

        public decimal GetCrossingAvailableQuantity(
            SubmitOrder incoming,
            IReadOnlyDictionary<OrderId, SimulatedOrder> openOrders)
        {
            var available = 0m;
            foreach (var orderId in GetCrossingOrderIds(incoming, openOrders))
            {
                var order = openOrders[orderId];
                var visibleQuantity = order.DisplayRemaining ?? order.RemainingQuantity;
                available += Math.Min(order.RemainingQuantity.Value, visibleQuantity.Value);
                if (available >= incoming.Quantity.Value)
                    break;
            }

            return available;
        }

        public IEnumerable<OrderId> GetCrossingOrderIds(
            SubmitOrder incoming,
            IReadOnlyDictionary<OrderId, SimulatedOrder> openOrders)
        {
            if (!incoming.LimitPrice.HasValue)
                yield break;

            var passiveSide = incoming.Side == Side.Buy ? Side.Sell : Side.Buy;
            var levels = _levels.Keys
                .Where(level => level.Instrument == incoming.Instrument
                    && level.Side == passiveSide
                    && Crosses(incoming, level))
                .OrderBy(level => level.Side == Side.Sell ? level.Price.Value : -level.Price.Value)
                .ToArray();

            foreach (var level in levels)
            {
                foreach (var orderId in _levels[level])
                {
                    if (openOrders.TryGetValue(orderId, out var order)
                        && !IsSelfMatch(incoming, order.Command)
                        && order.RemainingQuantity.Value > 0m
                        && order.Command.LimitPrice.HasValue)
                    {
                        yield return orderId;
                    }
                }
            }
        }

        public IEnumerable<OrderId> GetMarketOrderIds(
            SubmitOrder incoming,
            IReadOnlyDictionary<OrderId, SimulatedOrder> openOrders)
        {
            var passiveSide = incoming.Side == Side.Buy ? Side.Sell : Side.Buy;
            var levels = _levels.Keys
                .Where(level => level.Instrument == incoming.Instrument
                    && level.Side == passiveSide)
                .OrderBy(level => level.Side == Side.Sell ? level.Price.Value : -level.Price.Value)
                .ToArray();

            foreach (var level in levels)
            {
                foreach (var orderId in _levels[level])
                {
                    if (openOrders.TryGetValue(orderId, out var order)
                        && !IsSelfMatch(incoming, order.Command)
                        && order.RemainingQuantity.Value > 0m
                        && order.Command.LimitPrice.HasValue)
                    {
                        yield return orderId;
                    }
                }
            }
        }

        public decimal GetMarketAvailableQuantity(
            SubmitOrder incoming,
            IReadOnlyDictionary<OrderId, SimulatedOrder> openOrders)
        {
            var available = 0m;
            foreach (var orderId in GetMarketOrderIds(incoming, openOrders))
            {
                var order = openOrders[orderId];
                var visibleQuantity = order.DisplayRemaining ?? order.RemainingQuantity;
                available += Math.Min(order.RemainingQuantity.Value, visibleQuantity.Value);
                if (available >= incoming.Quantity.Value)
                    break;
            }

            return available;
        }

        public void Clear()
        {
            _levels.Clear();
            _orders.Clear();
        }

        private static bool TryCreateLevel(SimulatedOrder order, out ReplayBookLevel level)
        {
            var command = order.Command;
            if (command.Type == OrderType.Limit
                && command.LimitPrice.HasValue
                && order.RemainingQuantity.Value > 0m)
            {
                level = new ReplayBookLevel(command.Instrument, command.Side, command.LimitPrice.Value);
                return true;
            }

            level = default;
            return false;
        }

        private static bool IsPassivePrice(ReplayBookLevel level, Trade trade)
            => level.Side == Side.Buy
                ? level.Price.Value >= trade.Price.Value
                : level.Price.Value <= trade.Price.Value;

        private static bool Crosses(SubmitOrder incoming, ReplayBookLevel passiveLevel)
            => incoming.Side == Side.Buy
                ? incoming.LimitPrice!.Value.Value >= passiveLevel.Price.Value
                : incoming.LimitPrice!.Value.Value <= passiveLevel.Price.Value;

        private static bool IsSelfMatch(SubmitOrder incoming, SubmitOrder passive)
            => incoming.StrategyId == passive.StrategyId
                && incoming.VariantId == passive.VariantId;
    }

    private readonly record struct ReplayBookLevel(
        Instrument Instrument,
        Side Side,
        Price Price);

    private sealed class ActiveAlgoOrder
    {
        public required SubmitOrder Command { get; init; }
        public required string AlgorithmId { get; init; }
        public required Qty RemainingQuantity { get; set; }
        public required Instant StartedAt { get; init; }
        public required Instant EndsAt { get; init; }
        public required Duration Interval { get; init; }
        public required Instant NextSliceAt { get; set; }
        public required decimal ParticipationRate { get; init; }
        public required bool ForceCompleteAtHorizon { get; init; }
    }

    private sealed class SimulatedOrder
    {
        public required SubmitOrder Command { get; set; }
        public required Qty RemainingQuantity { get; set; }
        public required Money ReservedCash { get; set; }
        public required Instant SubmitTime { get; init; }
        public Qty? DisplayRemaining { get; set; }
        public decimal QueuePosition { get; set; }
        public long VenueSequence { get; set; }
        public bool StopTriggered { get; set; }
        public Price? TrailingReference { get; set; }
    }
}
