using Rhodium.Events;
using Rhodium.Primitives;
using Rhodium.Simulation.Diagnostics;
using Rhodium.Simulation.Frames;
using Rhodium.Simulation.Identity;

namespace Rhodium.Simulation.Exchange;

/// <summary>
/// Instrument-scoped matching engine owned by a simulated venue exchange.
/// </summary>
public sealed class SimulatedInstrumentEngine
{
    private const decimal DefaultPriceProtectionTickSize = 0.01m;
    private const decimal PriceScale = 1_000_000m;
    private const decimal QuantityScale = 1_000_000m;

    private readonly SimulationConfig _config;
    private readonly SimulationOrderPolicy _orderPolicy;
    private readonly SimulationVenuePolicy _policy;
    private readonly SimulationAccount _account;
    private readonly SimulationIdentityGenerator _identity;
    private readonly List<ExecutionEvent> _executionEvents = [];
    private readonly List<PendingOrder> _pendingOrders = [];
    private readonly List<SimulationRejectionDiagnostic> _rejections = [];
    private readonly Dictionary<OrderId, VenueOrderId> _venueOrderIds = [];
    private readonly FlatMarketByOrderBook _marketByOrderBook = new();
    private readonly Level[] _marketByOrderFillBuffer = new Level[1024];
    private readonly Dictionary<OrderListId, OrderId> _otoParentOrders = [];
    private readonly Dictionary<OrderListId, OrderId> _ouoParentOrders = [];
    private readonly Dictionary<OrderListId, List<SimulationOrderCommand>> _stagedOtoChildren = [];
    private readonly HashSet<OrderListId> _triggeredOtoLists = [];
    private FinanceEvent? _currentEvent;
    private Price? _bestBid;
    private Price? _bestAsk;
    private Instant _currentFrameTime = Instant.Epoch;
    private bool _currentEventAllowsExecution;

    /// <summary>Create an instrument matching engine.</summary>
    public SimulatedInstrumentEngine(
        Instrument instrument,
        SimulationConfig config,
        SimulationAccount account,
        MatchingFidelity matchingFidelity = MatchingFidelity.QueueAccurate,
        SimulationOrderPolicy? orderPolicy = null,
        SimulationVenuePolicy? policy = null,
        SimulationIdentityGenerator? identity = null)
    {
        Instrument = instrument;
        _config = config;
        _orderPolicy = orderPolicy ?? SimulationOrderPolicy.Default;
        _policy = policy ?? SimulationVenuePolicy.Default;
        _account = account;
        _identity = identity ?? new SimulationIdentityGenerator();
        MatchingFidelity = matchingFidelity;
        Status = config.InitialMarketStatus;
    }

    /// <summary>Instrument identity represented by this engine.</summary>
    public Instrument Instrument { get; }

    /// <summary>Current instrument trading status.</summary>
    public MarketStatus Status { get; private set; }

    /// <summary>Matching fidelity used by this engine.</summary>
    public MatchingFidelity MatchingFidelity { get; }

    /// <summary>Order admission policy used by this engine.</summary>
    public SimulationOrderPolicy OrderPolicy => _orderPolicy;

    /// <summary>Execution behavior policy used by this engine.</summary>
    public SimulationVenuePolicy SimulationPolicy => _policy;

    /// <summary>Most recent close mark supplied by replay data.</summary>
    public Price? CloseMark { get; private set; }

    /// <summary>Accepted order count.</summary>
    public int AcceptedOrders { get; private set; }

    /// <summary>Rejected order count.</summary>
    public int RejectedOrders { get; private set; }

    /// <summary>Filled order count.</summary>
    public int FilledOrders { get; private set; }

    /// <summary>Cancelled order count.</summary>
    public int CancelledOrders { get; private set; }

    /// <summary>Expired order count.</summary>
    public int ExpiredOrders { get; private set; }

    /// <summary>Current open resting order count.</summary>
    public int OpenOrders => _pendingOrders.Count;

    public int CountOpenOrders(StrategyId strategyId, int variantId)
    {
        var count = 0;
        for (var i = 0; i < _pendingOrders.Count; i++)
        {
            var command = _pendingOrders[i].Command;
            if (command.StrategyId == strategyId && command.VariantId == variantId)
                count++;
        }

        return count;
    }

    /// <summary>Number of rejection diagnostics emitted by this engine.</summary>
    public int RejectionDiagnosticCount => _rejections.Count;

    /// <summary>Copy rejection diagnostics emitted by this engine into caller-owned storage.</summary>
    internal int CopyRejections(Span<SimulationRejectionDiagnostic> destination)
    {
        if (destination.Length < _rejections.Count)
            throw new ArgumentException("Destination span is smaller than the rejection diagnostic count.", nameof(destination));

        for (var i = 0; i < _rejections.Count; i++)
            destination[i] = _rejections[i];

        return _rejections.Count;
    }

    internal void SetStatus(MarketStatus status)
        => Status = status;

    /// <summary>Return whether this engine policy allows execution from a replay event type.</summary>
    public bool AllowsExecution(FinanceEvent evt)
        => evt switch
        {
            BarClosed => _policy.BarExecution,
            TradeOccurred => _policy.TradeExecution,
            _ => true
        };

    /// <summary>Apply one semantic market/status event to the engine.</summary>
    public void OnMarketEvent(FinanceEvent evt, bool allowMatching = true)
    {
        switch (evt)
        {
            case VenueStatusChanged venue when venue.Venue == Instrument.Venue:
                Status = venue.Status;
                break;
            case InstrumentStatusChanged status when status.Instrument == Instrument:
                Status = status.Status;
                break;
            case InstrumentClosed closed when closed.Instrument == Instrument:
                Status = MarketStatus.Closed;
                CloseMark = closed.ClosePrice;
                break;
            case MarketEvent market when market.Instrument == Instrument:
                _currentEvent = market;
                _currentEventAllowsExecution = allowMatching;
                UpdateTopOfBook(market);
                UpdateMarketByOrderBook(market);
                if (allowMatching)
                    FillPendingTouchedOrders(market);
                break;
        }
    }

    /// <summary>Apply one frame-native L3 order-add event.</summary>
    public void OnBookOrderAdded(in BookOrderAddedFrame frame, bool allowMatching = true)
    {
        if (MatchingFidelity != MatchingFidelity.MarketByOrder)
            return;

        SetCurrentFrame(frame.TimestampNs, allowMatching);
        ApplyBookOrderAdded(frame.OrderId, frame.Side, frame.PriceTicks, frame.SizeLots, frame.VenueSequence);
    }

    /// <summary>Apply one frame-native L3 order-modify event.</summary>
    public void OnBookOrderModified(in BookOrderModifiedFrame frame, bool allowMatching = true)
    {
        if (MatchingFidelity != MatchingFidelity.MarketByOrder)
            return;

        SetCurrentFrame(frame.TimestampNs, allowMatching);
        ApplyBookOrderModified(frame.OrderId, frame.Side, frame.PriceTicks, frame.SizeLots, frame.VenueSequence);
    }

    /// <summary>Apply one frame-native L3 order-delete event.</summary>
    public void OnBookOrderDeleted(in BookOrderDeletedFrame frame, bool allowMatching = true)
    {
        if (MatchingFidelity != MatchingFidelity.MarketByOrder)
            return;

        SetCurrentFrame(frame.TimestampNs, allowMatching);
        ApplyBookOrderDeleted(new BookOrderId(frame.OrderId));
    }

    /// <summary>Apply one frame-native L3 order-execute event.</summary>
    public void OnBookOrderExecuted(in BookOrderExecutedFrame frame, bool allowMatching = true)
    {
        if (MatchingFidelity != MatchingFidelity.MarketByOrder)
            return;

        SetCurrentFrame(frame.TimestampNs, allowMatching);
        ApplyBookOrderExecuted(new BookOrderId(frame.OrderId), UnscaleQty(frame.ExecutedLots));
    }

    /// <summary>Submit an order command after venue latency has elapsed.</summary>
    public void Submit(SimulationOrderCommand command)
    {
        if (!ValidateOrderPolicy(command))
            return;

        if (Status != MarketStatus.Open)
        {
            Reject(command, $"Market is {Status}; replay order submission is disabled.");
            return;
        }

        if (command.Execution.PostOnly && command.Execution.OrderType == OrderType.Market)
        {
            Reject(command, "Post-only market orders are invalid because they would take liquidity.");
            return;
        }

        if (command.Execution.TimeInForce == TimeInForce.GTD && !command.Execution.GoodTilDate.HasValue)
        {
            Reject(command, "GTD orders require GoodTilDate.");
            return;
        }

        if (command.Execution.OrderType is OrderType.TrailingStopMarket or OrderType.TrailingStopLimit
            && (!command.Execution.TrailingOffset.HasValue || !command.Execution.TrailingOffsetType.HasValue))
        {
            Reject(command, $"{command.Execution.OrderType} requires trailing offset and offset type.");
            return;
        }

        if (!ValidateDisplayQuantity(command))
            return;

        if (!_policy.SupportContingentOrders
            && (command.OrderListId.HasValue || command.ContingencyType.HasValue))
        {
            Reject(command, $"{command.Instrument.Venue} replay policy does not support contingent orders.");
            return;
        }

        if (TryStageOtoChild(command))
            return;

        RegisterOuoParent(command);

        if (_policy.RejectTriggeredOrdersInMarket
            && Status == MarketStatus.Open
            && IsTriggeredOrder(command.Execution.OrderType))
        {
            Reject(command, $"{command.Instrument.Venue} replay policy rejects triggered orders while the market is open.");
            return;
        }

        if (_policy.UseReduceOnly && command.ReduceOnly && !IsReduceOnlySatisfied(command, out var reduceOnlyReason))
        {
            Reject(command, reduceOnlyReason);
            return;
        }

        Price? limitPrice = null;
        if (command.Execution.OrderType != OrderType.Market)
            limitPrice = ResolveLimitPrice(command);

        if (command.Execution.PostOnly
            && limitPrice.HasValue
            && WouldTakeLiquidity(command, limitPrice.Value))
        {
            Reject(command, "Post-only order would take liquidity.");
            return;
        }

        if (MatchingFidelity == MatchingFidelity.FastVectorApproximation
            && _currentEvent is not null
            && _currentEventAllowsExecution
            && ShouldFillOnEvent(command, limitPrice, stopTriggeredBeforeEvent: false, _currentEvent, out var fillPrice, out _, out var isMaker))
        {
            if (!TryCalculateProtectedFillPrice(command, fillPrice, command.Quantity, isMaker, out var protectedFillPrice, out var protectionReason))
            {
                Reject(command, protectionReason);
                return;
            }

            if (!_account.TryReserve(command, protectedFillPrice, _config.Margin, _config.Settlement, _policy.AllowCashBorrowing, out var reason))
            {
                Reject(command, reason);
                return;
            }

            _ = AcceptOrAssignMarketAck(command);
            Fill(command, command.Quantity, protectedFillPrice, isMaker, GetCurrentFillTime());
            TriggerOtoChildren(command, Qty.Zero);
            UpdateOuoSiblings(command, Qty.Zero);
            _venueOrderIds.Remove(command.ClientOrderId);
            return;
        }

        if (command.Execution.OrderType == OrderType.Market)
        {
            var marketRemaining = command.Quantity;
            var marketOrderAccepted = false;
            Price marketPrice = default;
            var hasCurrentMarketPrice = _currentEvent is not null
                && _currentEventAllowsExecution
                && TryGetCurrentMarketPrice(command.Side, out marketPrice);

            if (command.Execution.TimeInForce == TimeInForce.FOK
                && !CanFullyFillMarketOrder(command, hasCurrentMarketPrice))
            {
                _executionEvents.Add(new OrderCancelled(
                    command.ClientOrderId,
                    command.StrategyId,
                    command.VariantId,
                    command.Quantity,
                    "FOK market order was not fully fillable from resting liquidity.",
                    GetVenueOrderId(command.ClientOrderId),
                    command.AssetId));
                _venueOrderIds.Remove(command.ClientOrderId);
                return;
            }

            if (HasMarketableRestingLiquidity(command))
            {
                _ = AcceptOrAssignMarketAck(command);
                marketOrderAccepted = true;
                marketRemaining = MatchRestingMarketBook(command, command.Quantity);
                if (marketRemaining.Value <= 0m)
                {
                    TriggerOtoChildren(command, Qty.Zero);
                    UpdateOuoSiblings(command, Qty.Zero);
                    _venueOrderIds.Remove(command.ClientOrderId);
                    return;
                }
            }

            if (TryFillMarketByOrder(command with { Quantity = marketRemaining }, marketOrderAccepted))
                return;

            if (hasCurrentMarketPrice)
            {
                var remainingCommand = command with { Quantity = marketRemaining };
                if (!TryCalculateProtectedFillPrice(remainingCommand, marketPrice, marketRemaining, isMaker: false, out var protectedFillPrice, out var protectionReason))
                {
                    Reject(command, protectionReason);
                    return;
                }

                if (!_account.TryReserve(remainingCommand, protectedFillPrice, _config.Margin, _config.Settlement, _policy.AllowCashBorrowing, out var reason))
                {
                    Reject(command, reason);
                    return;
                }

                if (!marketOrderAccepted)
                    _ = AcceptOrAssignMarketAck(command);
                Fill(command, marketRemaining, protectedFillPrice, isMaker: false, GetCurrentFillTime());
                TriggerOtoChildren(command, Qty.Zero);
                UpdateOuoSiblings(command, Qty.Zero);
                _venueOrderIds.Remove(command.ClientOrderId);
            }
            else if (marketOrderAccepted)
            {
                _executionEvents.Add(new OrderCancelled(
                    command.ClientOrderId,
                    command.StrategyId,
                    command.VariantId,
                    marketRemaining,
                    "Market order exhausted available resting liquidity.",
                    GetVenueOrderId(command.ClientOrderId),
                    command.AssetId));
                TriggerOtoChildren(command, marketRemaining);
                UpdateOuoSiblings(command, marketRemaining);
                _venueOrderIds.Remove(command.ClientOrderId);
            }
            else
            {
                Reject(command, "No market price available.");
            }
            return;
        }

        if (command.Execution.OrderType == OrderType.MarketToLimit)
        {
            if (_currentEvent is not null
                && _currentEventAllowsExecution
                && TryGetMarketPrice(_currentEvent, command.Side, out var marketToLimitPrice))
            {
                if (!TryCalculateProtectedFillPrice(command, marketToLimitPrice, command.Quantity, isMaker: false, out var protectedFillPrice, out var protectionReason))
                {
                    Reject(command, protectionReason);
                    return;
                }

                if (!_account.TryReserve(command, protectedFillPrice, _config.Margin, _config.Settlement, _policy.AllowCashBorrowing, out var reason))
                {
                    Reject(command, reason);
                    return;
                }

                _ = Accept(command);
                Fill(command, command.Quantity, protectedFillPrice, isMaker: false, GetCurrentFillTime());
                TriggerOtoChildren(command, Qty.Zero);
                UpdateOuoSiblings(command, Qty.Zero);
                _venueOrderIds.Remove(command.ClientOrderId);
            }
            else
            {
                Reject(command, "No market price available for market-to-limit order.");
            }
            return;
        }

        if (!limitPrice.HasValue)
        {
            if (!IsMarketTriggeredOrder(command.Execution.OrderType))
            {
                Reject(command, "No limit price available for resting order.");
                return;
            }
        }

        var isReserved = false;
        if (limitPrice.HasValue
            && !_account.TryReserve(command, limitPrice.Value, _config.Margin, _config.Settlement, _policy.AllowCashBorrowing, out var reserveReason))
        {
            Reject(command, reserveReason);
            return;
        }
        else if (limitPrice.HasValue)
        {
            isReserved = true;
        }

        var venueOrderId = Accept(command);

        if (limitPrice.HasValue
            && command.Execution.TimeInForce == TimeInForce.FOK
            && !CanFullyFillFromRestingBook(command, limitPrice.Value, command.Quantity))
        {
            ProcessImmediateLimit(command, limitPrice.Value);
            return;
        }

        var remainingQuantity = command.Quantity;
        if (limitPrice.HasValue)
            remainingQuantity = MatchRestingBook(command, limitPrice.Value, command.Quantity);

        if (remainingQuantity.Value <= 0m)
        {
            TriggerOtoChildren(command, Qty.Zero);
            UpdateOuoSiblings(command, Qty.Zero);
            _venueOrderIds.Remove(command.ClientOrderId);
            return;
        }

        if (limitPrice.HasValue && command.Execution.TimeInForce is TimeInForce.IOC or TimeInForce.FOK)
        {
            if (_currentEvent is not null
                && _currentEventAllowsExecution
                && ShouldFillOnEvent(command, limitPrice, stopTriggeredBeforeEvent: false, _currentEvent, out _, out _, out _))
            {
                ProcessImmediateLimit(command with { Quantity = remainingQuantity }, limitPrice.Value);
                return;
            }

            CancelAccepted(command, remainingQuantity, $"{command.Execution.TimeInForce} order cancelled remaining quantity.");
            return;
        }

        AddPendingOrder(new PendingOrder(
            command,
            limitPrice,
            remainingQuantity,
            StopTriggered: false,
            IsReserved: isReserved,
            TrailingReference: null,
            DisplayRemaining: GetInitialDisplayRemaining(command, remainingQuantity),
            QueuePosition: _config.QueueModel.GetInitialPosition(),
            SubmitTime: GetCurrentFillTime(),
            VenueOrderId: venueOrderId));
    }

    private void AddPendingOrder(PendingOrder pending)
    {
        if (!pending.LimitPrice.HasValue)
        {
            _pendingOrders.Add(pending);
            return;
        }

        for (var i = 0; i < _pendingOrders.Count; i++)
        {
            if (ShouldInsertBefore(pending, _pendingOrders[i]))
            {
                _pendingOrders.Insert(i, pending);
                return;
            }
        }

        _pendingOrders.Add(pending);
    }

    private static bool ShouldInsertBefore(PendingOrder candidate, PendingOrder existing)
    {
        if (!candidate.LimitPrice.HasValue
            || !existing.LimitPrice.HasValue
            || candidate.Command.Side != existing.Command.Side)
        {
            return false;
        }

        var candidatePrice = candidate.LimitPrice.Value.Value;
        var existingPrice = existing.LimitPrice.Value.Value;
        if (candidatePrice == existingPrice)
            return false;

        return candidate.Command.Side == Side.Buy
            ? candidatePrice > existingPrice
            : candidatePrice < existingPrice;
    }

    private bool CanFullyFillFromRestingBook(SimulationOrderCommand command, Price limitPrice, Qty quantity)
    {
        var available = 0m;
        foreach (var pending in _pendingOrders)
        {
            if (!CanMatchRestingOrder(command, limitPrice, pending))
                continue;

            available += pending.RemainingQuantity.Value;
            if (available >= quantity.Value)
                return true;
        }

        return false;
    }

    private bool CanFullyFillMarketOrder(SimulationOrderCommand command, bool hasCurrentMarketPrice)
    {
        var restingQuantity = GetMarketableRestingQuantity(command);
        if (restingQuantity >= command.Quantity.Value)
            return true;

        var externalQuantity = new Qty(command.Quantity.Value - restingQuantity);
        if (MatchingFidelity == MatchingFidelity.MarketByOrder && _currentEventAllowsExecution)
        {
            return _marketByOrderBook.CanFullyConsume(
                command.Side,
                externalQuantity,
                GetEffectivePriceProtectionTicks(command),
                DefaultPriceProtectionTickSize);
        }

        return hasCurrentMarketPrice;
    }

    private decimal GetMarketableRestingQuantity(SimulationOrderCommand command)
    {
        var available = 0m;
        foreach (var pending in _pendingOrders)
        {
            if (CanMatchRestingMarketOrder(command, pending))
                available += pending.RemainingQuantity.Value;
        }

        return available;
    }

    private bool HasMarketableRestingLiquidity(SimulationOrderCommand command)
    {
        foreach (var pending in _pendingOrders)
        {
            if (CanMatchRestingMarketOrder(command, pending))
                return true;
        }

        return false;
    }

    private Qty MatchRestingMarketBook(SimulationOrderCommand command, Qty quantity)
    {
        var remaining = quantity;
        for (var i = 0; i < _pendingOrders.Count && remaining.Value > 0m;)
        {
            var pending = _pendingOrders[i];
            if (!CanMatchRestingMarketOrder(command, pending))
            {
                i++;
                continue;
            }

            var fillQuantity = new Qty(Math.Min(remaining.Value, pending.RemainingQuantity.Value));
            if (fillQuantity.Value <= 0m)
            {
                i++;
                continue;
            }

            var passivePrice = pending.LimitPrice!.Value;
            if (!_account.TryReserve(
                    command with { Quantity = fillQuantity },
                    passivePrice,
                    _config.Margin,
                    _config.Settlement,
                    _policy.AllowCashBorrowing,
                    out var reason))
            {
                Reject(command, reason);
                return remaining;
            }

            var fillPrice = CalculateFillPrice(passivePrice, fillQuantity, command.Side, isMaker: false);
            Fill(pending.Command, fillQuantity, fillPrice, isMaker: true, GetCurrentFillTime());
            Fill(command, fillQuantity, fillPrice, isMaker: false, GetCurrentFillTime());

            var passiveRemaining = pending.RemainingQuantity - fillQuantity;
            remaining -= fillQuantity;
            if (passiveRemaining.Value <= 0m)
            {
                _pendingOrders.RemoveAt(i);
                _venueOrderIds.Remove(pending.Command.ClientOrderId);
                TriggerOtoChildren(pending.Command, Qty.Zero);
                UpdateOuoSiblings(pending.Command, Qty.Zero);
            }
            else
            {
                _pendingOrders[i] = pending with
                {
                    RemainingQuantity = passiveRemaining,
                    DisplayRemaining = RefreshDisplayRemaining(
                        pending.Command,
                        passiveRemaining,
                        pending.DisplayRemaining,
                        fillQuantity)
                };
                TriggerOtoChildren(pending.Command, passiveRemaining);
                UpdateOuoSiblings(pending.Command, passiveRemaining);
                i++;
            }
        }

        return remaining;
    }

    private Qty MatchRestingBook(SimulationOrderCommand command, Price limitPrice, Qty quantity)
    {
        var remaining = quantity;
        for (var i = 0; i < _pendingOrders.Count && remaining.Value > 0m;)
        {
            var pending = _pendingOrders[i];
            if (!CanMatchRestingOrder(command, limitPrice, pending))
            {
                i++;
                continue;
            }

            var fillQuantity = new Qty(Math.Min(remaining.Value, pending.RemainingQuantity.Value));
            if (fillQuantity.Value <= 0m)
            {
                i++;
                continue;
            }

            var fillPrice = CalculateFillPrice(pending.LimitPrice!.Value, fillQuantity, command.Side, isMaker: false);
            Fill(pending.Command, fillQuantity, fillPrice, isMaker: true, GetCurrentFillTime());
            Fill(command, fillQuantity, fillPrice, isMaker: false, GetCurrentFillTime());

            var passiveRemaining = pending.RemainingQuantity - fillQuantity;
            remaining -= fillQuantity;
            if (passiveRemaining.Value <= 0m)
            {
                _pendingOrders.RemoveAt(i);
                _venueOrderIds.Remove(pending.Command.ClientOrderId);
                TriggerOtoChildren(pending.Command, Qty.Zero);
                UpdateOuoSiblings(pending.Command, Qty.Zero);
            }
            else
            {
                _pendingOrders[i] = pending with
                {
                    RemainingQuantity = passiveRemaining,
                    DisplayRemaining = RefreshDisplayRemaining(
                        pending.Command,
                        passiveRemaining,
                        pending.DisplayRemaining,
                        fillQuantity)
                };
                TriggerOtoChildren(pending.Command, passiveRemaining);
                UpdateOuoSiblings(pending.Command, passiveRemaining);
                i++;
            }
        }

        return remaining;
    }

    private static bool CanMatchRestingOrder(
        SimulationOrderCommand aggressor,
        Price aggressorLimit,
        PendingOrder passive)
    {
        if (!passive.LimitPrice.HasValue
            || passive.Command.Side == aggressor.Side
            || passive.Command.StrategyId == aggressor.StrategyId
            || !IsActiveRestingLiquidity(passive))
        {
            return false;
        }

        var passivePrice = passive.LimitPrice.Value.Value;
        return aggressor.Side == Side.Buy
            ? aggressorLimit.Value >= passivePrice
            : aggressorLimit.Value <= passivePrice;
    }

    private static bool CanMatchRestingMarketOrder(
        SimulationOrderCommand aggressor,
        PendingOrder passive)
        => passive.LimitPrice.HasValue
            && passive.Command.Side != aggressor.Side
            && passive.Command.StrategyId != aggressor.StrategyId
            && IsActiveRestingLiquidity(passive);

    private static bool IsActiveRestingLiquidity(PendingOrder passive)
        => !IsTriggeredOrder(passive.Command.Execution.OrderType)
            || passive.StopTriggered;

    /// <summary>Cancel an open order after venue latency has elapsed.</summary>
    public void Cancel(SimulationCancelCommand command)
    {
        if (CancelStagedOtoChild(command))
            return;

        for (var i = 0; i < _pendingOrders.Count; i++)
        {
            var pending = _pendingOrders[i];
            if (pending.Command.ClientOrderId != command.OrderId)
                continue;

            _pendingOrders.RemoveAt(i);
            _account.Release(command.OrderId);
            CancelledOrders++;
            _executionEvents.Add(new OrderCancelled(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                pending.RemainingQuantity,
                command.Reason ?? "Cancelled by user",
                pending.VenueOrderId,
                command.AssetId));
            _venueOrderIds.Remove(command.OrderId);
            return;
        }

        Reject(command, $"Order {command.OrderId} is not open.");
    }

    /// <summary>Modify an open order after venue latency has elapsed.</summary>
    public void Modify(SimulationModifyCommand command)
    {
        if (ModifyStagedOtoChild(command))
            return;

        for (var i = 0; i < _pendingOrders.Count; i++)
        {
            var pending = _pendingOrders[i];
            if (pending.Command.ClientOrderId != command.OrderId)
                continue;

            var nextQuantity = command.NewQuantity ?? pending.RemainingQuantity;
            if (nextQuantity.Value <= 0m)
            {
                Reject(command, "Modified quantity must be positive.");
                return;
            }

            var nextLimit = command.NewLimitPrice ?? pending.LimitPrice;
            if (!nextLimit.HasValue)
            {
                Reject(command, "Modified order requires a limit price.");
                return;
            }

            var nextCommand = pending.Command with
            {
                Quantity = nextQuantity,
                Execution = command.NewLimitPrice.HasValue
                    ? pending.Command.Execution.At(command.NewLimitPrice.Value)
                    : pending.Command.Execution
            };

            _account.Release(command.OrderId);
            if (!_account.TryReserve(nextCommand, nextLimit.Value, _config.Margin, _config.Settlement, _policy.AllowCashBorrowing, out var reason))
            {
                if (pending.LimitPrice.HasValue)
                    _ = _account.TryReserve(pending.Command, pending.LimitPrice.Value, _config.Margin, _config.Settlement, _policy.AllowCashBorrowing, out _);
                Reject(command, reason);
                return;
            }

            var updatedPending = pending with
            {
                Command = nextCommand,
                LimitPrice = nextLimit,
                RemainingQuantity = nextQuantity,
                DisplayRemaining = GetInitialDisplayRemaining(nextCommand, nextQuantity)
            };
            if (ShouldLosePriorityOnModify(pending, nextQuantity, command.NewLimitPrice))
            {
                _pendingOrders.RemoveAt(i);
                AddPendingOrder(updatedPending);
            }
            else
            {
                _pendingOrders[i] = updatedPending;
            }

            _executionEvents.Add(new OrderModified(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                NewQuantity: nextQuantity,
                NewLimitPrice: command.NewLimitPrice,
                VenueOrderId: pending.VenueOrderId,
                AssetId: command.AssetId));
            return;
        }

        Reject(command, $"Order {command.OrderId} is not open.");
    }

    private static bool ShouldLosePriorityOnModify(
        PendingOrder pending,
        Qty nextQuantity,
        Price? requestedLimitPrice)
    {
        if (requestedLimitPrice.HasValue
            && (!pending.LimitPrice.HasValue || requestedLimitPrice.Value != pending.LimitPrice.Value))
        {
            return true;
        }

        return nextQuantity.Value > pending.RemainingQuantity.Value;
    }

    private bool CancelStagedOtoChild(SimulationCancelCommand command)
    {
        SimulationOrderCommand? cancelledChild = null;
        OrderListId emptyOrderListId = default;
        var removeEmptyOrderList = false;

        foreach (var (orderListId, children) in _stagedOtoChildren)
        {
            var childIndex = children.FindIndex(child => child.ClientOrderId == command.OrderId);
            if (childIndex < 0)
                continue;

            var child = children[childIndex];
            children.RemoveAt(childIndex);
            if (children.Count == 0)
            {
                emptyOrderListId = orderListId;
                removeEmptyOrderList = true;
            }

            cancelledChild = child;
            break;
        }

        if (!cancelledChild.HasValue)
            return false;

        if (removeEmptyOrderList)
            _stagedOtoChildren.Remove(emptyOrderListId);

        var cancelled = cancelledChild.Value;
        CancelledOrders++;
        _executionEvents.Add(new OrderCancelled(
            cancelled.ClientOrderId,
            cancelled.StrategyId,
            cancelled.VariantId,
            cancelled.Quantity,
            "Cancelled while staged behind OTO parent.",
            AssetId: cancelled.AssetId));

        return true;
    }

    private bool ModifyStagedOtoChild(SimulationModifyCommand command)
    {
        foreach (var children in _stagedOtoChildren.Values)
        {
            var childIndex = children.FindIndex(child => child.ClientOrderId == command.OrderId);
            if (childIndex < 0)
                continue;

            var child = children[childIndex];
            var nextQuantity = command.NewQuantity ?? child.Quantity;
            var nextExecution = command.NewLimitPrice.HasValue
                ? child.Execution.At(command.NewLimitPrice.Value)
                : child.Execution;
            var next = child with
            {
                Quantity = nextQuantity,
                Execution = nextExecution
            };
            children[childIndex] = next;
            _executionEvents.Add(new OrderModified(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                command.NewQuantity,
                command.NewLimitPrice,
                AssetId: command.AssetId));
            return true;
        }

        return false;
    }

    private bool ValidateOrderPolicy(SimulationOrderCommand command)
    {
        if (_orderPolicy.AllowedOrderTypes is not null
            && !_orderPolicy.AllowedOrderTypes.Contains(command.Execution.OrderType))
        {
            Reject(command, $"{command.Venue} simulation order policy does not allow {command.Execution.OrderType} orders.");
            return false;
        }

        if (_orderPolicy.AllowedTimeInForce is not null
            && !_orderPolicy.AllowedTimeInForce.Contains(command.Execution.TimeInForce))
        {
            Reject(command, $"{command.Venue} simulation order policy does not allow {command.Execution.TimeInForce} orders.");
            return false;
        }

        if (!_orderPolicy.AllowPostOnly && command.Execution.PostOnly)
        {
            Reject(command, $"{command.Venue} simulation order policy does not allow post-only orders.");
            return false;
        }

        if (_orderPolicy.MinOrderQuantity is { } minimumQuantity
            && command.Quantity < minimumQuantity)
        {
            Reject(command, $"{command.Venue} simulation order policy requires minimum order quantity {minimumQuantity}.");
            return false;
        }

        if (_orderPolicy.MinOrderNotional is { } minimumNotional
            && !MeetsMinimumNotional(command, minimumNotional, out var reason))
        {
            Reject(command, reason);
            return false;
        }

        return true;
    }

    private bool ValidateDisplayQuantity(SimulationOrderCommand command)
    {
        if (!command.Execution.DisplayQuantity.HasValue)
            return true;

        var displayQuantity = command.Execution.DisplayQuantity.Value;
        if (displayQuantity.Value <= 0m)
        {
            Reject(command, "Iceberg display quantity must be positive.");
            return false;
        }

        if (displayQuantity.Value >= command.Quantity.Value)
        {
            Reject(command, "Iceberg display quantity must be smaller than total order quantity.");
            return false;
        }

        if (!IsDisplaySupportedOrder(command.Execution.OrderType) || !command.Execution.LimitPrice.HasValue)
        {
            Reject(command, "Iceberg display quantity is supported only for limit-style resting orders.");
            return false;
        }

        return true;
    }

    private static bool IsDisplaySupportedOrder(OrderType type)
        => type is OrderType.Limit
            or OrderType.StopLimit
            or OrderType.LimitIfTouched
            or OrderType.TrailingStopLimit;

    private bool MeetsMinimumNotional(
        SimulationOrderCommand command,
        Money minimumNotional,
        out string reason)
    {
        reason = string.Empty;
        if (minimumNotional.Amount <= 0m)
            return true;

        if (!TryResolveOrderPolicyReferencePrice(command, out var price))
        {
            reason = $"{command.Venue} simulation order policy requires a reference price for minimum notional checks.";
            return false;
        }

        if (!_account.TryGetContract(command.Instrument, out var contract))
        {
            reason = $"{command.Venue} simulation order policy requires a registered InstrumentContract for minimum notional checks.";
            return false;
        }

        var notional = DefaultInstrumentValuationModel.Instance.Notional(contract, command.Quantity, price);
        if (notional.Currency != minimumNotional.Currency)
        {
            reason = $"{command.Venue} simulation order policy minimum notional currency {minimumNotional.Currency} does not match order notional currency {notional.Currency}.";
            return false;
        }

        if (notional.Amount >= minimumNotional.Amount)
            return true;

        reason = $"{command.Venue} simulation order policy requires minimum order notional {minimumNotional}.";
        return false;
    }

    private bool TryResolveOrderPolicyReferencePrice(
        SimulationOrderCommand command,
        out Price price)
    {
        if (command.Execution.LimitPrice.HasValue)
        {
            price = command.Execution.LimitPrice.Value;
            return true;
        }

        if (command.Execution.StopPrice.HasValue)
        {
            price = command.Execution.StopPrice.Value;
            return true;
        }

        if (_currentEvent is not null && TryGetMarketPrice(_currentEvent, command.Side, out price))
            return true;

        price = default;
        return false;
    }

    /// <summary>Drain pending execution events into a caller-owned buffer.</summary>
    public int DrainExecutionEvents(Span<ExecutionEvent> destination)
    {
        var count = Math.Min(destination.Length, _executionEvents.Count);
        for (var i = 0; i < count; i++)
            destination[i] = _executionEvents[i];

        _executionEvents.RemoveRange(0, count);
        return count;
    }

    /// <summary>Whether the engine has pending execution events.</summary>
    public bool HasPendingWork => _executionEvents.Count > 0;

    /// <summary>Return true when the engine has due expirations or pending output.</summary>
    public bool HasDueWork(Instant now)
    {
        if (HasPendingWork)
            return true;

        for (var i = 0; i < _pendingOrders.Count; i++)
        {
            if (IsExpired(_pendingOrders[i], now))
                return true;
        }

        return false;
    }

    /// <summary>Expire GTD orders due at the supplied timestamp.</summary>
    public void ExpireDueOrders(Instant now)
    {
        for (var i = _pendingOrders.Count - 1; i >= 0; i--)
        {
            var pending = _pendingOrders[i];
            if (!IsExpired(pending, now))
                continue;

            _pendingOrders.RemoveAt(i);
            ExpireAccepted(pending.Command);
        }
    }

    /// <summary>Try to resolve the current mark price for margin and diagnostics.</summary>
    public bool TryGetMarkPrice(out Price price)
    {
        if (_currentEvent is not null && TryGetMarketPrice(_currentEvent, Side.Buy, out price))
            return true;

        if (CloseMark.HasValue)
        {
            price = CloseMark.Value;
            return true;
        }

        price = default;
        return false;
    }

    /// <summary>Try to resolve a mark price for closing a position with the supplied side.</summary>
    public bool TryGetPositionMarkPrice(Side positionSide, out Price price)
    {
        var executableSide = positionSide == Side.Buy ? Side.Sell : Side.Buy;
        if (_currentEvent is not null && TryGetMarketPrice(_currentEvent, executableSide, out price))
            return true;

        if (CloseMark.HasValue)
        {
            price = CloseMark.Value;
            return true;
        }

        price = default;
        return false;
    }

    /// <summary>Cancel all open orders for a strategy variant during margin liquidation.</summary>
    public void CancelOpenOrdersForMargin(StrategyId strategyId, int variantId)
    {
        for (var i = _pendingOrders.Count - 1; i >= 0; i--)
        {
            var pending = _pendingOrders[i];
            if (pending.Command.StrategyId != strategyId || pending.Command.VariantId != variantId)
                continue;

            _pendingOrders.RemoveAt(i);
            CancelAccepted(pending.Command, pending.RemainingQuantity, "Cancelled by margin liquidation.");
        }
    }

    /// <summary>Generate a liquidation fill for an account position at a mark price.</summary>
    public void Liquidate(AccountPositionSnapshot position, Price mark)
        => Liquidate(position, mark, position.Quantity.Abs);

    /// <summary>Generate a liquidation fill for part of an account position at a mark price.</summary>
    public void Liquidate(AccountPositionSnapshot position, Price mark, Qty quantity)
    {
        if (position.Quantity.IsZero || quantity.Value <= 0m)
            return;

        var side = position.Quantity.IsPositive ? Side.Sell : Side.Buy;
        if (quantity.Value > position.Quantity.Abs.Value)
            quantity = position.Quantity.Abs;

        var command = new SimulationOrderCommand(
            position.StrategyId,
            position.VariantId,
            AssetId: default,
            position.Instrument,
            position.Instrument.Venue,
            _identity.NextClientOrderId(),
            side,
            quantity,
            Execution.Market());

        Fill(command, quantity, CalculateFillPrice(mark, quantity, command.Side, isMaker: false), isMaker: false, GetCurrentFillTime());
    }

    private VenueOrderId Accept(SimulationOrderCommand command)
    {
        AcceptedOrders++;
        var venueOrderId = AssignVenueOrderId(command);
        _executionEvents.Add(new OrderAccepted(
            command.ClientOrderId,
            command.StrategyId,
            command.VariantId,
            venueOrderId,
            command.AssetId));
        return venueOrderId;
    }

    private VenueOrderId AcceptOrAssignMarketAck(SimulationOrderCommand command)
        => command.Execution.OrderType == OrderType.Market && !_policy.UseMarketOrderAcks
            ? AssignVenueOrderId(command)
            : Accept(command);

    private VenueOrderId AssignVenueOrderId(SimulationOrderCommand command)
    {
        var venueOrderId = _identity.NextVenueOrderId(command.Instrument);
        _venueOrderIds[command.ClientOrderId] = venueOrderId;
        return venueOrderId;
    }

    private void Reject(SimulationOrderCommand command, string reason)
    {
        RejectedOrders++;
        _rejections.Add(new SimulationRejectionDiagnostic(
            command.Venue,
            command.Instrument,
            command.ClientOrderId,
            reason));
        _executionEvents.Add(new OrderRejected(command.ClientOrderId, command.StrategyId, command.VariantId, reason, command.AssetId));
    }

    private bool IsReduceOnlySatisfied(SimulationOrderCommand command, out string reason)
    {
        var position = _account.GetPositionQuantity(command.StrategyId, command.VariantId, command.Instrument);
        var reducible = command.Side == Side.Sell
            ? Math.Max(0m, position.Value)
            : Math.Max(0m, -position.Value);
        if (command.Quantity.Value <= reducible)
        {
            reason = string.Empty;
            return true;
        }

        reason = $"Reduce-only order would increase exposure: requested {command.Quantity}, reducible {new Qty(reducible)}.";
        return false;
    }

    private static bool IsTriggeredOrder(OrderType type)
        => type is OrderType.StopMarket
            or OrderType.StopLimit
            or OrderType.MarketIfTouched
            or OrderType.LimitIfTouched
            or OrderType.TrailingStopMarket
            or OrderType.TrailingStopLimit;

    private static bool IsMarketTriggeredOrder(OrderType type)
        => type is OrderType.StopMarket
            or OrderType.MarketIfTouched
            or OrderType.TrailingStopMarket;

    private void Reject(SimulationCancelCommand command, string reason)
    {
        RejectedOrders++;
        _rejections.Add(new SimulationRejectionDiagnostic(
            command.Venue,
            command.Instrument,
            command.OrderId,
            reason));
        _executionEvents.Add(new OrderRejected(command.OrderId, command.StrategyId, command.VariantId, reason, command.AssetId));
    }

    private void Reject(SimulationModifyCommand command, string reason)
    {
        RejectedOrders++;
        _rejections.Add(new SimulationRejectionDiagnostic(
            command.Venue,
            command.Instrument,
            command.OrderId,
            reason));
        _executionEvents.Add(new OrderRejected(command.OrderId, command.StrategyId, command.VariantId, reason, command.AssetId));
    }

    private bool TryCalculateProtectedFillPrice(
        SimulationOrderCommand command,
        Price nominalPrice,
        Qty quantity,
        bool isMaker,
        out Price fillPrice,
        out string reason)
    {
        fillPrice = CalculateFillPrice(nominalPrice, quantity, command.Side, isMaker);
        reason = string.Empty;
        if (isMaker || command.Execution.OrderType != OrderType.Market)
            return true;

        var protectionTicks = GetEffectivePriceProtectionTicks(command);
        if (protectionTicks <= 0)
            return true;

        var maxMove = protectionTicks * DefaultPriceProtectionTickSize;
        var isWithinProtection = command.Side == Side.Buy
            ? fillPrice.Value <= nominalPrice.Value + maxMove
            : fillPrice.Value >= nominalPrice.Value - maxMove;
        if (isWithinProtection)
            return true;

        reason = $"Market order exceeded price protection: reference {nominalPrice}, fill {fillPrice}, protection {protectionTicks} ticks.";
        return false;
    }

    private int GetEffectivePriceProtectionTicks(SimulationOrderCommand command)
    {
        var venueTicks = _policy.PriceProtectionTicks;
        var orderTicks = command.Execution.MaxSlippageTicks;
        if (venueTicks > 0 && orderTicks > 0)
            return Math.Min(venueTicks, orderTicks);

        return Math.Max(venueTicks, orderTicks);
    }

    private Price CalculateFillPrice(Price price, Qty quantity, Side side, bool isMaker)
    {
        var improvedPrice = _config.PriceImprovement.Apply(price, side, isMaker);
        return _config.Slippage.Apply(improvedPrice, quantity, side);
    }

    private bool TryFillMarketByOrder(SimulationOrderCommand command, bool alreadyAccepted = false)
    {
        if (MatchingFidelity != MatchingFidelity.MarketByOrder)
            return false;
        if (command.Execution.OrderType != OrderType.Market)
            return false;
        if (!_currentEventAllowsExecution)
            return false;
        if (command.Execution.TimeInForce == TimeInForce.FOK
            && !_marketByOrderBook.CanFullyConsume(
                command.Side,
                command.Quantity,
                GetEffectivePriceProtectionTicks(command),
                DefaultPriceProtectionTickSize))
        {
            _executionEvents.Add(new OrderCancelled(
                command.ClientOrderId,
                command.StrategyId,
                command.VariantId,
                command.Quantity,
                "FOK market order was not fully fillable from market-by-order liquidity.",
                GetVenueOrderId(command.ClientOrderId),
                command.AssetId));
            _venueOrderIds.Remove(command.ClientOrderId);
            return true;
        }

        if (!_marketByOrderBook.TryConsumeLevels(
                command.Side,
                command.Quantity,
                GetEffectivePriceProtectionTicks(command),
                DefaultPriceProtectionTickSize,
                _marketByOrderFillBuffer,
                out var fillCount,
                out var filledQuantity))
            return false;

        if (!alreadyAccepted)
            _ = AcceptOrAssignMarketAck(command);
        for (var i = 0; i < fillCount; i++)
        {
            var level = _marketByOrderFillBuffer[i];
            if (!TryCalculateProtectedFillPrice(command, level.Price, level.Size, isMaker: false, out var protectedFillPrice, out var protectionReason))
            {
                Reject(command, protectionReason);
                return true;
            }

            if (!_account.TryReserve(command with { Quantity = level.Size }, protectedFillPrice, _config.Margin, _config.Settlement, _policy.AllowCashBorrowing, out var reason))
            {
                Reject(command, reason);
                return true;
            }

            Fill(command, level.Size, protectedFillPrice, isMaker: false, GetCurrentFillTime());
        }

        var remaining = command.Quantity - filledQuantity;
        if (remaining.Value > 0m)
        {
            _executionEvents.Add(new OrderCancelled(
                command.ClientOrderId,
                command.StrategyId,
                command.VariantId,
                remaining,
                "Market order exhausted available replay book liquidity.",
                GetVenueOrderId(command.ClientOrderId),
                command.AssetId));
        }

        TriggerOtoChildren(command, remaining);
        UpdateOuoSiblings(command, remaining);
        _venueOrderIds.Remove(command.ClientOrderId);
        return true;
    }

    private void UpdateTopOfBook(MarketEvent evt)
    {
        switch (evt)
        {
            case QuoteReceived quote:
                _bestBid = quote.Quote.Bid.Value > 0m ? quote.Quote.Bid : null;
                _bestAsk = quote.Quote.Ask.Value > 0m ? quote.Quote.Ask : null;
                break;
            case BookSnapshotReceived book:
                _bestBid = book.Book.Bid;
                _bestAsk = book.Book.Ask;
                break;
            case BookDepthSnapshotReceived snapshot:
                _bestBid = snapshot.Bids.Count > 0 ? snapshot.Bids[0].Price : null;
                _bestAsk = snapshot.Asks.Count > 0 ? snapshot.Asks[0].Price : null;
                break;
            case BookDepth10Received snapshot:
                _bestBid = snapshot.Bids.Count > 0 ? snapshot.Bids[0].Price : null;
                _bestAsk = snapshot.Asks.Count > 0 ? snapshot.Asks[0].Price : null;
                break;
        }
    }

    private bool TryGetCurrentMarketPrice(Side side, out Price price)
    {
        var topOfBook = side == Side.Buy ? _bestAsk : _bestBid;
        if (topOfBook.HasValue && topOfBook.Value.Value > 0m)
        {
            price = topOfBook.Value;
            return true;
        }

        if (_currentEvent is not null)
            return TryGetMarketPrice(_currentEvent, side, out price);

        price = default;
        return false;
    }

    private void UpdateMarketByOrderBook(MarketEvent evt)
    {
        if (MatchingFidelity != MatchingFidelity.MarketByOrder)
            return;

        switch (evt)
        {
            case BookSnapshotReceived book:
                ReplaceMarketByOrderSnapshot(book.Book.Bids, book.Book.Asks, int.MaxValue);
                break;
            case BookLevelDeltaReceived delta:
                ApplyBookLevelDelta(delta.Delta);
                break;
            case BookLevelDeltasReceived deltas:
                for (var i = 0; i < deltas.Deltas.Count; i++)
                    ApplyBookLevelDelta(deltas.Deltas[i]);
                break;
            case BookDepthSnapshotReceived snapshot:
                ReplaceMarketByOrderSnapshot(snapshot.Bids, snapshot.Asks, snapshot.Depth);
                break;
            case BookDepth10Received snapshot:
                ReplaceMarketByOrderSnapshot(snapshot.Bids, snapshot.Asks, 10);
                break;
        }
    }

    private void ReplaceMarketByOrderSnapshot(
        IReadOnlyList<Level> bids,
        IReadOnlyList<Level> asks,
        int depth)
    {
        _marketByOrderBook.Clear();
        var sequence = 1L;
        AddMarketByOrderSnapshotSide(Side.Buy, bids, depth, ref sequence);
        AddMarketByOrderSnapshotSide(Side.Sell, asks, depth, ref sequence);
    }

    private void AddMarketByOrderSnapshotSide(
        Side side,
        IReadOnlyList<Level> levels,
        int depth,
        ref long sequence)
    {
        var count = Math.Min(depth, levels.Count);
        for (var i = 0; i < count; i++)
        {
            var level = levels[i];
            if (level.Size.Value <= 0m)
                continue;

            var externalOrderId = new BookOrderId(-sequence);
            _marketByOrderBook.AddOrUpdate(
                externalOrderId,
                side,
                level.Price,
                level.Size,
                sequence);
            sequence++;
        }
    }

    private void ApplyBookLevelDelta(BookLevelDelta delta)
    {
        if (delta.Action == BookAction.Clear)
        {
            _marketByOrderBook.Clear();
            return;
        }

        var orderId = GetLevelDeltaOrderId(delta.Side, delta.Price);
        if (delta.Action == BookAction.Delete || delta.Size.Value <= 0m)
        {
            _marketByOrderBook.Delete(orderId);
            return;
        }

        _marketByOrderBook.AddOrUpdate(
            orderId,
            delta.Side,
            delta.Price,
            delta.Size,
            delta.VenueSequence);
    }

    private static BookOrderId GetLevelDeltaOrderId(Side side, Price price)
    {
        const long baseId = long.MinValue / 2;
        const long sideStride = 1_000_000_000_000_000L;
        var priceTicks = decimal.ToInt64(decimal.Round(price.Value * PriceScale, 0, MidpointRounding.AwayFromZero));
        var sideOffset = side == Side.Buy ? 0L : sideStride;
        return new BookOrderId(baseId + sideOffset + priceTicks);
    }

    private void SetCurrentFrame(long timestampNs, bool allowMatching)
    {
        _currentEvent = null;
        _currentFrameTime = new Instant(timestampNs);
        _currentEventAllowsExecution = allowMatching;
    }

    private Instant GetCurrentFillTime()
        => _currentEvent is null ? _currentFrameTime : GetFillTime(_currentEvent);

    private void ApplyBookOrderAdded(
        long orderId,
        Side side,
        long priceTicks,
        long sizeLots,
        long venueSequence)
    {
        var size = UnscaleQty(sizeLots);
        if (size.Value <= 0m)
            return;

        var externalOrderId = new BookOrderId(orderId);
        _marketByOrderBook.AddOrUpdate(
            externalOrderId,
            side,
            UnscalePrice(priceTicks),
            size,
            venueSequence);
    }

    private void ApplyBookOrderModified(
        long orderId,
        Side side,
        long priceTicks,
        long sizeLots,
        long venueSequence)
    {
        var externalOrderId = new BookOrderId(orderId);
        var size = UnscaleQty(sizeLots);
        if (size.Value <= 0m)
        {
            _marketByOrderBook.Delete(externalOrderId);
            return;
        }

        _marketByOrderBook.AddOrUpdate(
            externalOrderId,
            side,
            UnscalePrice(priceTicks),
            size,
            venueSequence);
    }

    private static Price UnscalePrice(long priceTicks)
        => new(priceTicks / PriceScale, Currency.USD);

    private static Qty UnscaleQty(long sizeLots)
        => new(sizeLots / QuantityScale);

    private void ApplyBookOrderDeleted(BookOrderId orderId)
        => _marketByOrderBook.Delete(orderId);

    private void ApplyBookOrderExecuted(BookOrderId orderId, Qty executedSize)
        => _marketByOrderBook.Execute(orderId, executedSize);

    private void Fill(SimulationOrderCommand command, Qty quantity, Price fillPrice, bool isMaker, Instant now)
    {
        FilledOrders++;
        var contract = _account.ResolveContract(command.Instrument);
        var feeCurrency = contract.Exposure.SettlementCurrency();
        var thirtyDayVolume = _account.GetThirtyDayFeeVolume(
            command.StrategyId,
            command.VariantId,
            feeCurrency,
            now);
        var commission = _config.Fees.Calculate(contract, quantity, fillPrice, command.Side, isMaker, thirtyDayVolume);
        _account.ApplyFill(command, quantity, fillPrice, commission, now);
        _executionEvents.Add(new OrderFilled(
            command.ClientOrderId,
            command.Instrument,
            command.VariantId,
            command.StrategyId,
            command.Side,
            quantity,
            fillPrice,
            commission,
            _identity.NextExecutionId(command.Instrument),
            GetVenueOrderId(command.ClientOrderId),
            command.AssetId));
        CancelOcoSiblings(command);
    }

    private void CancelAccepted(SimulationOrderCommand command, Qty remainingQuantity, string reason)
    {
        CancelledOrders++;
        _account.Release(command.ClientOrderId);
        _executionEvents.Add(new OrderCancelled(
            command.ClientOrderId,
            command.StrategyId,
            command.VariantId,
            remainingQuantity,
            reason,
            GetVenueOrderId(command.ClientOrderId),
            command.AssetId));
        _venueOrderIds.Remove(command.ClientOrderId);
    }

    private void ExpireAccepted(SimulationOrderCommand command)
    {
        ExpiredOrders++;
        _account.Release(command.ClientOrderId);
        _executionEvents.Add(new OrderExpired(
            command.ClientOrderId,
            command.StrategyId,
            command.VariantId,
            GetVenueOrderId(command.ClientOrderId),
            command.AssetId));
        _venueOrderIds.Remove(command.ClientOrderId);
    }

    private static bool IsExpired(PendingOrder order, Instant now)
    {
        if (order.Command.Execution.TimeInForce == TimeInForce.GTD)
            return order.Command.Execution.GoodTilDate is { } goodTilDate
                && goodTilDate <= now;

        return order.Command.Execution.TimeInForce == TimeInForce.Day
            && now.ToDateTimeOffset().UtcDateTime.Date > order.SubmitTime.ToDateTimeOffset().UtcDateTime.Date;
    }

    private bool TryStageOtoChild(SimulationOrderCommand command)
    {
        if (!command.OrderListId.HasValue || command.ContingencyType != ContingencyType.OTO)
            return false;

        var orderListId = command.OrderListId.Value;
        if (_triggeredOtoLists.Contains(orderListId))
            return false;

        if (!_otoParentOrders.TryGetValue(orderListId, out var parentOrderId))
        {
            _otoParentOrders[orderListId] = command.ClientOrderId;
            return false;
        }

        if (parentOrderId == command.ClientOrderId)
            return false;

        if (!_stagedOtoChildren.TryGetValue(orderListId, out var children))
        {
            children = [];
            _stagedOtoChildren[orderListId] = children;
        }

        children.Add(command);
        return true;
    }

    private void RegisterOuoParent(SimulationOrderCommand command)
    {
        if (!command.OrderListId.HasValue || command.ContingencyType != ContingencyType.OUO)
            return;

        _ouoParentOrders.TryAdd(command.OrderListId.Value, command.ClientOrderId);
    }

    private void TriggerOtoChildren(SimulationOrderCommand filledCommand, Qty remainingParentQuantity)
    {
        if (!filledCommand.OrderListId.HasValue || filledCommand.ContingencyType != ContingencyType.OTO)
            return;

        var orderListId = filledCommand.OrderListId.Value;
        if (!_otoParentOrders.TryGetValue(orderListId, out var parentOrderId)
            || parentOrderId != filledCommand.ClientOrderId)
        {
            return;
        }

        if (_policy.OtoFullTrigger && remainingParentQuantity.Value > 0m)
            return;

        _triggeredOtoLists.Add(orderListId);
        if (!_stagedOtoChildren.Remove(orderListId, out var children))
            return;

        foreach (var child in children)
            Submit(child);
    }

    private void UpdateOuoSiblings(SimulationOrderCommand filledCommand, Qty remainingParentQuantity)
    {
        if (!filledCommand.OrderListId.HasValue || filledCommand.ContingencyType != ContingencyType.OUO)
            return;

        var orderListId = filledCommand.OrderListId.Value;
        if (!_ouoParentOrders.TryGetValue(orderListId, out var parentOrderId)
            || parentOrderId != filledCommand.ClientOrderId)
        {
            return;
        }

        var parentFilledQuantity = filledCommand.Quantity - remainingParentQuantity;
        if (parentFilledQuantity.Value <= 0m)
            return;

        for (var i = _pendingOrders.Count - 1; i >= 0; i--)
        {
            var sibling = _pendingOrders[i];
            if (sibling.Command.ClientOrderId == filledCommand.ClientOrderId
                || sibling.Command.OrderListId != orderListId
                || sibling.Command.StrategyId != filledCommand.StrategyId
                || sibling.Command.VariantId != filledCommand.VariantId)
            {
                continue;
            }

            ResizePendingOrder(i, sibling, parentFilledQuantity);
        }
    }

    private void ResizePendingOrder(int index, PendingOrder pending, Qty newQuantity)
    {
        var alreadyFilled = pending.Command.Quantity - pending.RemainingQuantity;
        var nextRemaining = new Qty(Math.Max(0m, newQuantity.Value - alreadyFilled.Value));
        if (nextRemaining.Value <= 0m)
        {
            _pendingOrders.RemoveAt(index);
            _account.Release(pending.Command.ClientOrderId);
            _venueOrderIds.Remove(pending.Command.ClientOrderId);
            return;
        }

        var nextCommand = pending.Command with { Quantity = newQuantity };
        _account.Release(pending.Command.ClientOrderId);
        if (pending.LimitPrice.HasValue
            && !_account.TryReserve(
                nextCommand,
                pending.LimitPrice.Value,
                _config.Margin,
                _config.Settlement,
                _policy.AllowCashBorrowing,
                out var reason))
        {
            _pendingOrders.RemoveAt(index);
            Reject(nextCommand, reason);
            _venueOrderIds.Remove(pending.Command.ClientOrderId);
            return;
        }

        _pendingOrders[index] = pending with
        {
            Command = nextCommand,
            RemainingQuantity = nextRemaining
        };
        _executionEvents.Add(new OrderModified(
            nextCommand.ClientOrderId,
            nextCommand.StrategyId,
            nextCommand.VariantId,
            NewQuantity: newQuantity,
            NewLimitPrice: pending.LimitPrice,
            VenueOrderId: pending.VenueOrderId,
            AssetId: nextCommand.AssetId));
    }

    private void CancelOcoSiblings(SimulationOrderCommand filledCommand)
    {
        if (!filledCommand.OrderListId.HasValue || filledCommand.ContingencyType != ContingencyType.OCO)
            return;

        for (var i = _pendingOrders.Count - 1; i >= 0; i--)
        {
            var sibling = _pendingOrders[i];
            if (sibling.Command.ClientOrderId == filledCommand.ClientOrderId
                || sibling.Command.OrderListId != filledCommand.OrderListId)
            {
                continue;
            }

            _pendingOrders.RemoveAt(i);
            CancelAccepted(
                sibling.Command,
                sibling.RemainingQuantity,
                $"Cancelled by OCO sibling {filledCommand.ClientOrderId.Value} fill.");
        }
    }

    private VenueOrderId GetVenueOrderId(OrderId orderId)
        => _venueOrderIds.TryGetValue(orderId, out var venueOrderId)
            ? venueOrderId
            : default;

    private void ProcessImmediateLimit(SimulationOrderCommand command, Price limitPrice)
    {
        if (_currentEvent is null
            || !ShouldFillOnEvent(command, limitPrice, stopTriggeredBeforeEvent: false, _currentEvent, out var fillPrice, out _, out var isMaker))
        {
            CancelAccepted(command, command.Quantity, $"{command.Execution.TimeInForce} order was not immediately fillable.");
            return;
        }

        var fillQuantity = DetermineFillQuantity(command.Quantity, _currentEvent, decimal.MaxValue);
        if (command.Execution.TimeInForce == TimeInForce.FOK && fillQuantity.Value < command.Quantity.Value)
        {
            CancelAccepted(command, command.Quantity, "FOK order was not fully fillable.");
            return;
        }

        Fill(
            command,
            fillQuantity,
            CalculateFillPrice(fillPrice, fillQuantity, command.Side, isMaker),
            isMaker,
            GetCurrentFillTime());
        var remaining = command.Quantity - fillQuantity;
        TriggerOtoChildren(command, remaining);
        UpdateOuoSiblings(command, remaining);
        if (remaining.Value > 0m)
            CancelAccepted(command, remaining, "IOC order cancelled remaining quantity.");
        else
            _venueOrderIds.Remove(command.ClientOrderId);
    }

    private Price? ResolveLimitPrice(SimulationOrderCommand command)
    {
        if (command.Execution.LimitPrice.HasValue)
            return command.Execution.LimitPrice.Value;

        return null;
    }

    private bool WouldTakeLiquidity(SimulationOrderCommand command, Price limitPrice)
    {
        if (_currentEvent is null)
            return false;

        return TryGetMarketPrice(_currentEvent, command.Side, out var marketPrice)
            && (command.Side == Side.Buy
                ? limitPrice.Value >= marketPrice.Value
                : limitPrice.Value <= marketPrice.Value);
    }

    private void FillPendingTouchedOrders(FinanceEvent evt)
    {
        if (Status != MarketStatus.Open)
            return;

        var remainingEventLiquidity = _policy.LiquidityConsumption
            && _config.FillBehavior == FillBehavior.PartialFillOnTrade
            && evt is TradeOccurred trade
                ? trade.Trade.Size.Value
                : decimal.MaxValue;

        for (var i = 0; i < _pendingOrders.Count;)
        {
            var pending = _pendingOrders[i];
            var trailingUpdated = TryUpdateTrailingStop(pending, evt, out var updatedPending);
            if (trailingUpdated)
            {
                pending = updatedPending;
                _pendingOrders[i] = pending;
            }

            if (ShouldYieldToTriggeredOcoSibling(pending, evt))
            {
                i++;
                continue;
            }

            if (!ShouldFillOnEvent(
                    pending.Command,
                    pending.LimitPrice,
                    pending.StopTriggered,
                    evt,
                    out var fillPrice,
                    out var stopTriggered,
                    out var isMaker))
            {
                if (stopTriggered != pending.StopTriggered)
                    _pendingOrders[i] = pending with { StopTriggered = stopTriggered };
                i++;
                continue;
            }

            var fillQuantity = DetermineFillQuantity(pending.RemainingQuantity, evt, remainingEventLiquidity);
            if (pending.DisplayRemaining.HasValue)
                fillQuantity = new Qty(Math.Min(fillQuantity.Value, pending.DisplayRemaining.Value.Value));

            if (fillQuantity.Value <= 0m)
                break;

            if (!pending.IsReserved
                && !_account.TryReserve(
                    pending.Command with { Quantity = fillQuantity },
                    fillPrice,
                    _config.Margin,
                    _config.Settlement,
                    _policy.AllowCashBorrowing,
                    out var reserveReason))
            {
                Reject(pending.Command, reserveReason);
                _pendingOrders.RemoveAt(i);
                _venueOrderIds.Remove(pending.Command.ClientOrderId);
                continue;
            }

            Fill(
                pending.Command,
                fillQuantity,
                CalculateFillPrice(fillPrice, fillQuantity, pending.Command.Side, isMaker),
                isMaker,
                GetFillTime(evt));
            if (remainingEventLiquidity != decimal.MaxValue)
                remainingEventLiquidity = Math.Max(0m, remainingEventLiquidity - fillQuantity.Value);

            var remaining = pending.RemainingQuantity - fillQuantity;
            if (remaining.Value <= 0m)
            {
                var currentIndex = FindPendingOrderIndex(pending.Command.ClientOrderId);
                if (currentIndex >= 0)
                    _pendingOrders.RemoveAt(currentIndex);
                _venueOrderIds.Remove(pending.Command.ClientOrderId);
                TriggerOtoChildren(pending.Command, Qty.Zero);
                UpdateOuoSiblings(pending.Command, Qty.Zero);
                i = currentIndex >= 0 ? currentIndex : i;
            }
            else
            {
                var currentIndex = FindPendingOrderIndex(pending.Command.ClientOrderId);
                if (currentIndex < 0)
                    continue;

                _pendingOrders[currentIndex] = pending with
                {
                    RemainingQuantity = remaining,
                    StopTriggered = stopTriggered,
                    DisplayRemaining = RefreshDisplayRemaining(pending.Command, remaining, pending.DisplayRemaining, fillQuantity)
                };
                TriggerOtoChildren(pending.Command, remaining);
                UpdateOuoSiblings(pending.Command, remaining);
                i = currentIndex + 1;
            }
        }
    }

    private int FindPendingOrderIndex(OrderId orderId)
    {
        for (var i = 0; i < _pendingOrders.Count; i++)
        {
            if (_pendingOrders[i].Command.ClientOrderId == orderId)
                return i;
        }

        return -1;
    }

    private bool ShouldYieldToTriggeredOcoSibling(PendingOrder pending, FinanceEvent evt)
    {
        if (evt is not BarClosed
            || pending.Command.Execution.OrderType != OrderType.Limit
            || !pending.Command.OrderListId.HasValue
            || pending.Command.ContingencyType != ContingencyType.OCO)
        {
            return false;
        }

        var orderListId = pending.Command.OrderListId.Value;
        foreach (var sibling in _pendingOrders)
        {
            if (sibling.Command.ClientOrderId == pending.Command.ClientOrderId
                || sibling.Command.OrderListId != orderListId
                || sibling.Command.ContingencyType != ContingencyType.OCO
                || !IsTriggeredOrder(sibling.Command.Execution.OrderType))
            {
                continue;
            }

            if (ShouldFillOnEvent(
                    sibling.Command,
                    sibling.LimitPrice,
                    sibling.StopTriggered,
                    evt,
                    out _,
                    out _,
                    out _))
            {
                return true;
            }
        }

        return false;
    }

    private static Qty? GetInitialDisplayRemaining(SimulationOrderCommand command, Qty remainingQuantity)
    {
        if (!command.Execution.DisplayQuantity.HasValue)
            return null;

        if (remainingQuantity.Value <= 0m)
            return Qty.Zero;

        return new Qty(Math.Min(command.Execution.DisplayQuantity.Value.Value, remainingQuantity.Value));
    }

    private static Qty? RefreshDisplayRemaining(
        SimulationOrderCommand command,
        Qty remainingQuantity,
        Qty? displayRemaining,
        Qty filledQuantity)
    {
        if (!displayRemaining.HasValue)
            return null;

        var nextDisplay = new Qty(Math.Max(0m, displayRemaining.Value.Value - filledQuantity.Value));
        if (nextDisplay.Value <= 0m && remainingQuantity.Value > 0m)
            return GetInitialDisplayRemaining(command, remainingQuantity);

        return nextDisplay;
    }

    private static bool TryUpdateTrailingStop(
        PendingOrder pending,
        FinanceEvent evt,
        out PendingOrder updated)
    {
        updated = pending;
        if (pending.Command.Execution.OrderType is not (OrderType.TrailingStopMarket or OrderType.TrailingStopLimit))
            return false;

        if (!TryGetTrailingReference(pending.Command.Side, evt, out var reference))
            return false;

        var trailingReference = pending.TrailingReference.HasValue
            ? pending.Command.Side == Side.Buy
                ? Price.Min(pending.TrailingReference.Value, reference)
                : Price.Max(pending.TrailingReference.Value, reference)
            : reference;

        if (!TryCalculateTrailingOffset(pending.Command.Execution, trailingReference, out var offset))
            return false;

        var stopPrice = pending.Command.Side == Side.Buy
            ? new Price(trailingReference.Value + offset, trailingReference.Currency)
            : new Price(trailingReference.Value - offset, trailingReference.Currency);
        updated = pending with
        {
            Command = pending.Command with
            {
                Execution = pending.Command.Execution.WithStopPrice(stopPrice)
            },
            TrailingReference = trailingReference
        };
        return true;
    }

    private static bool TryCalculateTrailingOffset(
        ExecutionSpec execution,
        Price reference,
        out decimal offset)
    {
        offset = default;
        if (!execution.TrailingOffset.HasValue || !execution.TrailingOffsetType.HasValue)
            return false;

        offset = execution.TrailingOffsetType.Value switch
        {
            TrailingOffsetType.Price => execution.TrailingOffset.Value,
            TrailingOffsetType.Percent => reference.Value * execution.TrailingOffset.Value / 100m,
            TrailingOffsetType.Ticks => execution.TrailingOffset.Value * DefaultPriceProtectionTickSize,
            _ => 0m
        };

        return offset > 0m;
    }

    private Qty DetermineFillQuantity(Qty remainingQuantity, FinanceEvent evt, decimal remainingEventLiquidity)
    {
        if (_config.FillBehavior == FillBehavior.PartialFillOnTrade && evt is TradeOccurred trade)
        {
            var available = _policy.LiquidityConsumption
                ? Math.Min(trade.Trade.Size.Value, remainingEventLiquidity)
                : trade.Trade.Size.Value;
            return new Qty(Math.Min(remainingQuantity.Value, Math.Max(0m, available)));
        }

        return remainingQuantity;
    }

    private static bool ShouldFillOnEvent(
        SimulationOrderCommand command,
        Price? limitPrice,
        bool stopTriggeredBeforeEvent,
        FinanceEvent evt,
        out Price fillPrice,
        out bool stopTriggeredAfterEvent,
        out bool isMaker)
    {
        stopTriggeredAfterEvent = stopTriggeredBeforeEvent;
        isMaker = true;

        if (command.Execution.OrderType == OrderType.Market)
        {
            isMaker = false;
            return TryGetMarketPrice(evt, command.Side, out fillPrice);
        }

        if (command.Execution.OrderType is OrderType.StopMarket
            or OrderType.StopLimit
            or OrderType.TrailingStopMarket
            or OrderType.TrailingStopLimit)
            return ShouldFillStopOnEvent(command, limitPrice, stopTriggeredBeforeEvent, evt, out fillPrice, out stopTriggeredAfterEvent, out isMaker);

        if (command.Execution.OrderType is OrderType.MarketIfTouched or OrderType.LimitIfTouched)
            return ShouldFillIfTouchedOnEvent(command, limitPrice, evt, out fillPrice, out isMaker);

        if (!limitPrice.HasValue)
        {
            fillPrice = default;
            return false;
        }

        return IsLimitTouched(command.Side, limitPrice.Value, evt, out fillPrice);
    }

    private static bool ShouldFillStopOnEvent(
        SimulationOrderCommand command,
        Price? limitPrice,
        bool stopTriggeredBeforeEvent,
        FinanceEvent evt,
        out Price fillPrice,
        out bool stopTriggeredAfterEvent,
        out bool isMaker)
    {
        stopTriggeredAfterEvent = stopTriggeredBeforeEvent;
        isMaker = true;
        var stopPrice = command.Execution.StopPrice;
        if (!stopPrice.HasValue)
        {
            fillPrice = default;
            return false;
        }

        var triggeredThisEvent = IsStopTriggered(command.Side, stopPrice.Value, evt);
        stopTriggeredAfterEvent = stopTriggeredBeforeEvent || triggeredThisEvent;
        if (!stopTriggeredAfterEvent)
        {
            fillPrice = default;
            return false;
        }

        if (command.Execution.OrderType is OrderType.StopMarket or OrderType.TrailingStopMarket)
        {
            isMaker = false;
            fillPrice = stopPrice.Value;
            return true;
        }

        if (!limitPrice.HasValue)
        {
            fillPrice = default;
            return false;
        }

        return IsLimitTouched(command.Side, limitPrice.Value, evt, out fillPrice);
    }

    private static bool ShouldFillIfTouchedOnEvent(
        SimulationOrderCommand command,
        Price? limitPrice,
        FinanceEvent evt,
        out Price fillPrice,
        out bool isMaker)
    {
        isMaker = false;
        fillPrice = default;
        var triggerPrice = command.Execution.StopPrice;
        if (!triggerPrice.HasValue || !IsIfTouchedTriggered(command.Side, triggerPrice.Value, evt))
            return false;

        if (command.Execution.OrderType == OrderType.MarketIfTouched)
        {
            fillPrice = triggerPrice.Value;
            return true;
        }

        isMaker = true;
        if (!limitPrice.HasValue)
            return false;

        return IsLimitTouched(command.Side, limitPrice.Value, evt, out fillPrice);
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
            case BookSnapshotReceived book:
                return TryGetBookPrice(book.Book.Bids, book.Book.Asks, int.MaxValue, side, out price);
            case BookDepthSnapshotReceived snapshot:
                return TryGetBookPrice(snapshot.Bids, snapshot.Asks, snapshot.Depth, side, out price);
            case BookDepth10Received snapshot:
                return TryGetBookPrice(snapshot.Bids, snapshot.Asks, 10, side, out price);
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
            case BookSnapshotReceived book:
                return TryGetBookPrice(book.Book.Bids, book.Book.Asks, int.MaxValue, side, out price);
            case BookDepthSnapshotReceived snapshot:
                return TryGetBookPrice(snapshot.Bids, snapshot.Asks, snapshot.Depth, side, out price);
            case BookDepth10Received snapshot:
                return TryGetBookPrice(snapshot.Bids, snapshot.Asks, 10, side, out price);
            default:
                price = default;
                return false;
        }
    }

    private static bool IsStopTriggered(Side side, Price stopPrice, FinanceEvent evt)
        => evt switch
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
            BookSnapshotReceived book => TryGetBookPrice(book.Book.Bids, book.Book.Asks, int.MaxValue, side, out var price)
                && (side == Side.Buy
                    ? price.Value >= stopPrice.Value
                    : price.Value <= stopPrice.Value),
            BookDepthSnapshotReceived snapshot => TryGetBookPrice(snapshot.Bids, snapshot.Asks, snapshot.Depth, side, out var price)
                && (side == Side.Buy
                    ? price.Value >= stopPrice.Value
                    : price.Value <= stopPrice.Value),
            BookDepth10Received snapshot => TryGetBookPrice(snapshot.Bids, snapshot.Asks, 10, side, out var price)
                && (side == Side.Buy
                    ? price.Value >= stopPrice.Value
                    : price.Value <= stopPrice.Value),
            _ => false
        };

    private static bool IsIfTouchedTriggered(Side side, Price triggerPrice, FinanceEvent evt)
        => evt switch
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
            BookSnapshotReceived book => TryGetBookPrice(book.Book.Bids, book.Book.Asks, int.MaxValue, side, out var price)
                && (side == Side.Buy
                    ? price.Value <= triggerPrice.Value
                    : price.Value >= triggerPrice.Value),
            BookDepthSnapshotReceived snapshot => TryGetBookPrice(snapshot.Bids, snapshot.Asks, snapshot.Depth, side, out var price)
                && (side == Side.Buy
                    ? price.Value <= triggerPrice.Value
                    : price.Value >= triggerPrice.Value),
            BookDepth10Received snapshot => TryGetBookPrice(snapshot.Bids, snapshot.Asks, 10, side, out var price)
                && (side == Side.Buy
                    ? price.Value <= triggerPrice.Value
                    : price.Value >= triggerPrice.Value),
            _ => false
        };

    private static Instant GetFillTime(FinanceEvent? evt)
        => evt switch
        {
            QuoteReceived quote => quote.Quote.Time.ExchangeTime,
            TradeOccurred trade => trade.Trade.Time.ExchangeTime,
            BarClosed bar => bar.Bar.Time,
            BookSnapshotReceived book => book.Book.Time,
            BookDepthSnapshotReceived depth => depth.Time,
            BookDepth10Received depth => depth.Time,
            _ => evt?.Time ?? Instant.Epoch
        };

    private static bool IsLimitTouched(Side side, Price limit, FinanceEvent evt, out Price fillPrice)
    {
        switch (evt)
        {
            case BarClosed bar:
                fillPrice = limit;
                return side == Side.Buy
                    ? bar.Bar.Low.Value <= limit.Value
                    : bar.Bar.High.Value >= limit.Value;
            case QuoteReceived quote:
                fillPrice = limit;
                return side == Side.Buy
                    ? quote.Quote.Ask.Value <= limit.Value
                    : quote.Quote.Bid.Value >= limit.Value;
            case TradeOccurred trade:
                fillPrice = limit;
                return side == Side.Buy
                    ? trade.Trade.Price.Value <= limit.Value
                    : trade.Trade.Price.Value >= limit.Value;
            case BookSnapshotReceived book:
                fillPrice = limit;
                return TryGetBookPrice(book.Book.Bids, book.Book.Asks, int.MaxValue, side, out var bookPrice)
                    && (side == Side.Buy
                        ? bookPrice.Value <= limit.Value
                        : bookPrice.Value >= limit.Value);
            case BookDepthSnapshotReceived snapshot:
                fillPrice = limit;
                return TryGetBookPrice(snapshot.Bids, snapshot.Asks, snapshot.Depth, side, out var depthPrice)
                    && (side == Side.Buy
                        ? depthPrice.Value <= limit.Value
                        : depthPrice.Value >= limit.Value);
            case BookDepth10Received snapshot:
                fillPrice = limit;
                return TryGetBookPrice(snapshot.Bids, snapshot.Asks, 10, side, out var depth10Price)
                    && (side == Side.Buy
                        ? depth10Price.Value <= limit.Value
                        : depth10Price.Value >= limit.Value);
            default:
                fillPrice = default;
                return false;
        }
    }

    private static bool TryGetBookPrice(
        IReadOnlyList<Level> bids,
        IReadOnlyList<Level> asks,
        int depth,
        Side aggressorSide,
        out Price price)
    {
        var levels = aggressorSide == Side.Buy ? asks : bids;
        if (depth <= 0 || levels.Count == 0)
        {
            price = default;
            return false;
        }

        var level = levels[0];
        if (level.Size.Value > 0m && level.Price.Value > 0m)
        {
            price = level.Price;
            return true;
        }

        price = default;
        return false;
    }

    private readonly record struct PendingOrder(
        SimulationOrderCommand Command,
        Price? LimitPrice,
        Qty RemainingQuantity,
        bool StopTriggered,
        bool IsReserved,
        Price? TrailingReference,
        Qty? DisplayRemaining,
        decimal QueuePosition,
        Instant SubmitTime,
        VenueOrderId VenueOrderId);

}
