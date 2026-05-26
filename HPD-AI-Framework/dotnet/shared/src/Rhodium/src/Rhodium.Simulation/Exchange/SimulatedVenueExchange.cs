using Rhodium.Events;
using Rhodium.Options;
using Rhodium.Primitives;
using Rhodium.Simulation.Frames;
using Rhodium.Simulation.Identity;

namespace Rhodium.Simulation.Exchange;

/// <summary>
/// Venue-scoped simulated exchange that owns account state, latency queues, modules, and instrument engines.
/// </summary>
public sealed class SimulatedVenueExchange
{
    private readonly SimulationConfig _config;
    private readonly Dictionary<Instrument, SimulatedInstrumentEngine> _engines = [];
    private readonly List<ExecutionEvent> _executionEvents = [];
    private readonly List<PendingExecutionResponse> _pendingExecutionResponses = [];
    private readonly List<FinanceEvent> _simulationEvents = [];
    private readonly List<ActiveAlgoOrder> _activeAlgoOrders = [];
    private readonly ExecutionEvent[] _engineBuffer = new ExecutionEvent[64];
    private readonly FinanceEvent[] _accountEventBuffer = new FinanceEvent[64];
    private readonly SimulationCommandHeap _commands = new();
    private readonly ContractLifecycleScheduler _lifecycleScheduler = new();
    private readonly OptionLifecycleProcessor _optionLifecycleProcessor = new();
    private readonly SimulationIdentityGenerator _identity;
    private readonly Dictionary<Instrument, SimulationInstrumentConfig> _instrumentConfigs;
    private readonly Dictionary<(StrategyId StrategyId, int VariantId, Currency Currency), ActiveMarginCall> _activeMarginCalls = [];
    private readonly Dictionary<(StrategyId StrategyId, int VariantId, Instrument Instrument), Price> _marginMarks = [];
    private readonly Dictionary<OrderId, Qty> _orderFilledQuantities = [];
    private readonly Dictionary<OrderId, Qty> _orderQuantities = [];
    private readonly HashSet<OrderId> _terminalOrderIds = [];
    private readonly HashSet<(StrategyId StrategyId, int VariantId, Currency Currency)> _emittedMarginKeys = [];
    private readonly HashSet<(StrategyId StrategyId, int VariantId, Instrument Instrument, Instant Expiry)> _blockedLifecycleNotices = [];
    private readonly List<(StrategyId StrategyId, int VariantId, Currency Currency)> _resolvedMarginKeys = [];
    private readonly List<MarginAccountStatus> _marginStatuses = [];
    private readonly Dictionary<(StrategyId StrategyId, int VariantId), SimulationAccount.MarginStatusAccumulator> _marginStatusAccumulators = [];
    private readonly List<AccountPositionSnapshot> _liquidationPositions = [];
    private readonly List<AccountPositionSnapshot> _statementPositions = [];
    private readonly List<AccountPositionSnapshot> _lifecyclePositions = [];
    private readonly List<ScheduledContractLifecycle> _dueLifecycleWork = [];
    private readonly List<AssetDelivered> _pendingDeliveredCustodySnapshots = [];
    private readonly Dictionary<Instrument, Price> _lastMarks = [];
    private readonly Dictionary<Instrument, Price> _settlementReferencePrices = [];
    private readonly Dictionary<SimulationOptionAssignmentKey, SimulationOptionAssignmentInput> _assignmentInputs = [];
    private readonly Dictionary<Instrument, Price> _statementMarks = [];
    private MarketStatus _venueStatus;
    private bool _marginDirty;
    private bool _drainingFlushedExecutionResponses;
    private Instant _currentTime = Instant.Epoch;
    private long _nextResponseSequence;
    private long _totalEntryLatencyNanos;
    private long _minEntryLatencyNanos = long.MaxValue;
    private long _maxEntryLatencyNanos;

    /// <summary>Create a simulated venue exchange.</summary>
    public SimulatedVenueExchange(
        Venue venue,
        SimulationConfig config,
        Money initialCash,
        MatchingFidelity defaultMatchingFidelity = MatchingFidelity.QueueAccurate,
        SimulationIdentityGenerator? identity = null,
        SimulationOrderPolicy? orderPolicy = null,
        SimulationVenuePolicy? simulationPolicy = null,
        IReadOnlyList<SimulationInstrumentConfig>? instrumentConfigs = null)
    {
        Venue = venue;
        _config = config;
        Account = new SimulationAccount(initialCash, config.AccountType, config.Settlement);
        DefaultMatchingFidelity = defaultMatchingFidelity;
        OrderPolicy = orderPolicy ?? SimulationOrderPolicy.Default;
        SimulationPolicy = simulationPolicy ?? SimulationVenuePolicy.Default;
        _identity = identity ?? new SimulationIdentityGenerator();
        _instrumentConfigs = [];
        if (instrumentConfigs is not null)
        {
            for (var i = 0; i < instrumentConfigs.Count; i++)
            {
                var instrumentConfig = instrumentConfigs[i];
                if (instrumentConfig.Instrument.Venue == venue)
                {
                    _instrumentConfigs[instrumentConfig.Instrument] = instrumentConfig;
                    Account.RegisterContract(instrumentConfig.Contract);
                    _lifecycleScheduler.Register(instrumentConfig.Contract);
                }
            }
        }

        _venueStatus = config.InitialMarketStatus;
    }

    /// <summary>Venue identity represented by this exchange.</summary>
    public Venue Venue { get; }

    /// <summary>Current venue-level trading status.</summary>
    public MarketStatus Status => _venueStatus;

    /// <summary>Venue-owned account ledger and reservation state.</summary>
    public SimulationAccount Account { get; }

    /// <summary>Default matching fidelity for newly created instrument engines.</summary>
    public MatchingFidelity DefaultMatchingFidelity { get; }

    /// <summary>Venue-level order admission policy.</summary>
    public SimulationOrderPolicy OrderPolicy { get; }

    /// <summary>Venue-level execution behavior policy.</summary>
    public SimulationVenuePolicy SimulationPolicy { get; }

    /// <summary>When true, zero-entry-latency caller commands process immediately instead of waiting for a replay-turn drain.</summary>
    public bool ProcessZeroLatencyCommandsImmediately { get; set; } = true;

    /// <summary>Total commands submitted to this venue.</summary>
    public int SubmittedCommands { get; private set; }

    /// <summary>Number of instrument engines currently created under this venue.</summary>
    public int InstrumentCount => _engines.Count;

    /// <summary>Number of sampled entry-latency observations.</summary>
    public int LatencySampleCount { get; private set; }

    /// <summary>Minimum observed entry latency.</summary>
    public Duration MinEntryLatency => LatencySampleCount == 0 ? Duration.Zero : Duration.FromNanos(_minEntryLatencyNanos);

    /// <summary>Maximum observed entry latency.</summary>
    public Duration MaxEntryLatency => LatencySampleCount == 0 ? Duration.Zero : Duration.FromNanos(_maxEntryLatencyNanos);

    /// <summary>Average observed entry latency.</summary>
    public Duration AverageEntryLatency => LatencySampleCount == 0
        ? Duration.Zero
        : Duration.FromNanos(_totalEntryLatencyNanos / LatencySampleCount);

    /// <summary>Total observed entry latency in nanoseconds.</summary>
    public long TotalEntryLatencyNanos => _totalEntryLatencyNanos;

    /// <summary>Register or replace the canonical contract for an instrument traded on this venue.</summary>
    public void RegisterContract(InstrumentContract contract)
    {
        if (contract.Instrument.Venue != Venue)
            throw new InvalidOperationException($"Instrument contract {contract.Instrument} does not belong to venue {Venue}.");

        Account.RegisterContract(contract);
        _lifecycleScheduler.Register(contract);
        if (!_instrumentConfigs.ContainsKey(contract.Instrument))
        {
            _instrumentConfigs[contract.Instrument] = SimulationInstrumentConfig.For(contract);
            return;
        }

        var existing = _instrumentConfigs[contract.Instrument];
        _instrumentConfigs[contract.Instrument] = existing with { Contract = contract };
    }

    /// <summary>Get or lazily create the instrument engine for an instrument.</summary>
    public SimulatedInstrumentEngine GetOrCreateInstrumentEngine(Instrument instrument)
    {
        if (_engines.TryGetValue(instrument, out var engine))
            return engine;

        var instrumentConfig = _instrumentConfigs.TryGetValue(instrument, out var configured)
            ? configured
            : null;
        if (instrumentConfig is not null)
        {
            Account.RegisterContract(instrumentConfig.Contract);
            _lifecycleScheduler.Register(instrumentConfig.Contract);
        }
        else if (!Account.TryGetContract(instrument, out _))
        {
            RegisterContract(Contracts.FromIdentity(instrument, Account.Cash.Currency));
            instrumentConfig = _instrumentConfigs[instrument];
        }

        var engineConfig = instrumentConfig?.Config ?? _config;
        var initialStatus = instrumentConfig?.InitialStatus
            ?? instrumentConfig?.Config?.InitialMarketStatus
            ?? _venueStatus;

        engine = new SimulatedInstrumentEngine(
            instrument,
            engineConfig with { InitialMarketStatus = initialStatus },
            Account,
            instrumentConfig?.MatchingFidelity ?? DefaultMatchingFidelity,
            instrumentConfig?.OrderPolicy ?? OrderPolicy,
            instrumentConfig?.SimulationPolicy ?? SimulationPolicy,
            _identity);
        _engines[instrument] = engine;
        return engine;
    }

    /// <summary>Number of instrument engines currently owned by this venue.</summary>
    public int InstrumentEngineCount => _engines.Count;

    internal Dictionary<Instrument, SimulatedInstrumentEngine>.ValueCollection EngineValues
        => _engines.Values;

    internal bool TryGetInstrumentEngine(Instrument instrument, out SimulatedInstrumentEngine engine)
        => _engines.TryGetValue(instrument, out engine!);

    /// <summary>Apply one semantic replay event to this venue.</summary>
    public void OnMarketEvent(FinanceEvent evt)
    {
        var now = GetEventTime(evt);
        _currentTime = now;
        Account.ReleaseSettlements(now);
        DrainAccountEvents();
        ExpireDueOrders(now);
        DrainEngineEvents();
        DrainAccountEvents();

        if (evt is VenueStatusChanged venueStatus && venueStatus.Venue == Venue)
        {
            _venueStatus = venueStatus.Status;
            foreach (var existingEngine in _engines.Values)
                existingEngine.OnMarketEvent(evt);
            _marginDirty = true;
            ProcessMargin(now);
            return;
        }

        if (TryGetLifecycleMarketStatus(evt, out var lifecycleStatus))
        {
            _venueStatus = lifecycleStatus;
            foreach (var existingEngine in _engines.Values)
                existingEngine.SetStatus(lifecycleStatus);
            _marginDirty = true;
            ProcessMargin(now);
            return;
        }

        if (evt is SettlementReferencePricePublished settlementReference)
        {
            ObserveSettlementReference(settlementReference, now);
            return;
        }

        if (evt is OptionAssignmentNoticePublished assignmentNotice &&
            assignmentNotice.Instrument.Venue == Venue)
        {
            ObserveAssignmentNotice(assignmentNotice, now);
            return;
        }

        if (evt is CorporateActionApplied corporateAction && corporateAction.Instrument.Venue == Venue)
        {
            var firstEffectIndex = _simulationEvents.Count;
            Account.ApplyCorporateAction(corporateAction, _simulationEvents);
            EmitCorporateActionAccountSurfaces(firstEffectIndex, _simulationEvents.Count, corporateAction.EffectiveAt);
            _marginDirty = true;
            ProcessMargin(now);
            return;
        }

        if (evt is not MarketEvent market || market.Instrument.Venue != Venue)
            return;

        if (TryGetMarketMark(market, out var eventMark))
            _lastMarks[market.Instrument] = eventMark;

        var engine = GetOrCreateInstrumentEngine(market.Instrument);
        engine.OnMarketEvent(evt, AllowsExecution(evt) && engine.AllowsExecution(evt));
        DrainEngineEvents();
        DrainAccountEvents();
        EmitDeliveredCustodySnapshots(now);
        ProcessActiveAlgoOrders(now, evt);
        DrainEngineEvents();
        DrainAccountEvents();
        EmitDeliveredCustodySnapshots(now);
        if (ProcessDueOptionLifecycle(now))
            DrainAccountEvents();
        _marginDirty = true;
        ProcessMargin(now);
    }

    internal void ObserveMarketMark(MarketEvent market, Instant now)
    {
        _currentTime = now;
        if (!TryGetMarketMark(market, out var mark))
            return;

        _lastMarks[market.Instrument] = mark;
        if (!ProcessDueOptionLifecycle(now))
            return;

        DrainAccountEvents();
        ProcessMargin(now);
    }

    internal void ObserveSettlementReference(SettlementReferencePricePublished settlementReference, Instant now)
    {
        _currentTime = now;
        _settlementReferencePrices[settlementReference.Instrument] = settlementReference.Price;
        if (!ProcessDueOptionLifecycle(now))
            return;

        DrainAccountEvents();
        ProcessMargin(now);
    }

    internal void ObserveAssignmentNotice(OptionAssignmentNoticePublished assignmentNotice, Instant now)
    {
        _currentTime = now;
        _assignmentInputs[new SimulationOptionAssignmentKey(
            assignmentNotice.StrategyId,
            assignmentNotice.VariantId,
                assignmentNotice.Instrument)] = new SimulationOptionAssignmentInput(
                assignmentNotice.IsSelectedForRandomAssignment,
                assignmentNotice.ProRataAssignmentRatio,
                ToAssignmentRule(assignmentNotice),
                reason: assignmentNotice.Reason);
        if (!ProcessDueOptionLifecycle(now))
            return;

        DrainAccountEvents();
        ProcessMargin(now);
    }

    private static OptionAssignmentRule? ToAssignmentRule(OptionAssignmentNoticePublished assignmentNotice)
    {
        if (assignmentNotice.MinimumIntrinsicValue is null &&
            assignmentNotice.AssignShortPositions is null)
        {
            return null;
        }

        return new OptionAssignmentRule(
            assignmentNotice.MinimumIntrinsicValue ?? Money.Zero(Currency.None),
            assignmentNotice.AssignShortPositions ?? true);
    }

    /// <summary>Apply one frame-native L3 order-add event.</summary>
    public void OnBookOrderAdded(Instrument instrument, in BookOrderAddedFrame frame, bool allowMatching = true)
    {
        PrepareFrameMarketEvent(frame.TimestampNs);
        GetOrCreateInstrumentEngine(instrument).OnBookOrderAdded(in frame, allowMatching);
        CompleteFrameMarketEvent(frame.TimestampNs);
    }

    /// <summary>Apply one frame-native L3 order-modify event.</summary>
    public void OnBookOrderModified(Instrument instrument, in BookOrderModifiedFrame frame, bool allowMatching = true)
    {
        PrepareFrameMarketEvent(frame.TimestampNs);
        GetOrCreateInstrumentEngine(instrument).OnBookOrderModified(in frame, allowMatching);
        CompleteFrameMarketEvent(frame.TimestampNs);
    }

    /// <summary>Apply one frame-native L3 order-delete event.</summary>
    public void OnBookOrderDeleted(Instrument instrument, in BookOrderDeletedFrame frame, bool allowMatching = true)
    {
        PrepareFrameMarketEvent(frame.TimestampNs);
        GetOrCreateInstrumentEngine(instrument).OnBookOrderDeleted(in frame, allowMatching);
        CompleteFrameMarketEvent(frame.TimestampNs);
    }

    /// <summary>Apply one frame-native L3 order-execute event.</summary>
    public void OnBookOrderExecuted(Instrument instrument, in BookOrderExecutedFrame frame, bool allowMatching = true)
    {
        PrepareFrameMarketEvent(frame.TimestampNs);
        GetOrCreateInstrumentEngine(instrument).OnBookOrderExecuted(in frame, allowMatching);
        CompleteFrameMarketEvent(frame.TimestampNs);
    }

    private void PrepareFrameMarketEvent(long timestampNs)
    {
        var now = new Instant(timestampNs);
        _currentTime = now;
        Account.ReleaseSettlements(now);
        DrainAccountEvents();
        ExpireDueOrders(now);
        DrainEngineEvents();
        DrainAccountEvents();
    }

    private void CompleteFrameMarketEvent(long timestampNs)
    {
        var now = new Instant(timestampNs);
        _currentTime = now;
        DrainEngineEvents();
        DrainAccountEvents();
        EmitDeliveredCustodySnapshots(now);
        ProcessActiveAlgoOrders(now, null);
        DrainEngineEvents();
        DrainAccountEvents();
        EmitDeliveredCustodySnapshots(now);
        _marginDirty = true;
        ProcessMargin(now);
    }

    /// <summary>Enqueue an order submit command using venue entry latency.</summary>
    public void Submit(in SimulationOrderCommand command, Instant now)
    {
        SubmittedCommands++;
        if (SimulationPolicy.FrozenAccount)
        {
            _executionEvents.Add(new OrderRejected(
                command.ClientOrderId,
                command.StrategyId,
                command.VariantId,
                $"{Venue} replay account is frozen.",
                command.AssetId));
            return;
        }

        if (command.Execution.Algorithm == ExecutionAlgorithm.None)
            _orderQuantities[command.ClientOrderId] = command.Quantity;

        _orderFilledQuantities.TryAdd(command.ClientOrderId, Qty.Zero);
        var arrivesAt = now + _config.Latency.EntryMean;
        RecordEntryLatency(now, arrivesAt);
        if (ProcessZeroLatencyCommandsImmediately && _config.Latency.EntryMean <= Duration.Zero)
        {
            _currentTime = now;
            ProcessSubmit(command, now);
            return;
        }

        _commands.EnqueueSubmit(command, arrivesAt);
    }

    /// <summary>Enqueue or apply an order cancel command using venue entry latency.</summary>
    public void Cancel(in SimulationCancelCommand command, Instant now)
    {
        SubmittedCommands++;
        if (_commands.TryRemoveInflightSubmit(command.OrderId, out var submit))
        {
            RecordEntryLatency(Duration.Zero);
            _executionEvents.Add(new OrderCancelled(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                submit.Quantity,
                command.Reason ?? "Cancelled before simulated venue arrival.",
                AssetId: command.AssetId));
            return;
        }

        var arrivesAt = now + _config.Latency.EntryMean;
        RecordEntryLatency(now, arrivesAt);
        if (ProcessZeroLatencyCommandsImmediately && _config.Latency.EntryMean <= Duration.Zero)
        {
            _currentTime = now;
            ProcessCancel(command);
            return;
        }

        _commands.EnqueueCancel(command, arrivesAt);
    }

    /// <summary>Enqueue or apply an order modify command using venue entry latency.</summary>
    public void Modify(in SimulationModifyCommand command, Instant now)
    {
        SubmittedCommands++;
        if (_commands.TryModifyInflightSubmit(command, out var modified))
        {
            RecordEntryLatency(Duration.Zero);
            _orderQuantities[command.OrderId] = modified.Quantity;
            _executionEvents.Add(new OrderModified(
                command.OrderId,
                command.StrategyId,
                command.VariantId,
                modified.Quantity,
                modified.Execution.LimitPrice,
                AssetId: command.AssetId));
            return;
        }

        var arrivesAt = now + _config.Latency.EntryMean;
        RecordEntryLatency(now, arrivesAt);
        if (ProcessZeroLatencyCommandsImmediately && _config.Latency.EntryMean <= Duration.Zero)
        {
            _currentTime = now;
            ProcessModify(command);
            return;
        }

        _commands.EnqueueModify(command, arrivesAt);
    }

    /// <summary>Return true when the venue has due commands, expirations, or pending output.</summary>
    public bool HasDueWork(Instant now)
    {
        if (_commands.HasDue(now)
            || _executionEvents.Count > 0
            || _simulationEvents.Count > 0
            || HasActionableDueOptionLifecycleWork(now))
        {
            return true;
        }

        foreach (var engine in _engines.Values)
        {
            if (engine.HasDueWork(now))
                return true;
        }

        return false;
    }

    private bool HasActionableDueOptionLifecycleWork(Instant now)
    {
        _lifecycleScheduler.CopyDue(now, _dueLifecycleWork);
        for (var i = 0; i < _dueLifecycleWork.Count; i++)
        {
            var due = _dueLifecycleWork[i];
            var contract = Account.ResolveContract(due.Instrument);
            if (contract.Payoff is not PayoffTerms.Option option)
                continue;

            Account.CopyPositions(due.Instrument, _lifecyclePositions);
            for (var positionIndex = 0; positionIndex < _lifecyclePositions.Count; positionIndex++)
            {
                var position = _lifecyclePositions[positionIndex];
                var key = (position.StrategyId, position.VariantId, position.Instrument, due.Expiry);
                if (!_blockedLifecycleNotices.Contains(key))
                    return true;

                if (HasOptionLifecycleReference(contract, option.Terms))
                    return true;
            }
        }

        return false;
    }

    /// <summary>Process all due venue work at the supplied timestamp.</summary>
    public void DrainDueWork(Instant now)
    {
        _currentTime = now;
        Account.ReleaseSettlements(now);
        DrainAccountEvents();

        while (_commands.TryDequeueDue(now, out var command))
        {
            if (command.Submit is { } submit)
            {
                ProcessSubmit(submit, now);
            }
            else if (command.Cancel is { } cancel)
            {
                _commands.RemoveSameArrivalModifies(cancel.OrderId, command.ArrivesAt);
                ProcessCancel(cancel);
            }
            else if (command.Modify is { } modify)
            {
                ProcessModify(modify);
            }
        }

        var executionCount = _executionEvents.Count;
        DrainEngineEvents();
        DrainAccountEvents();
        ExpireDueOrders(now);
        DrainEngineEvents();
        DrainAccountEvents();
        EmitDeliveredCustodySnapshots(now);
        if (ProcessDueOptionLifecycle(now))
            DrainAccountEvents();
        if (_executionEvents.Count != executionCount)
            _marginDirty = true;
        ProcessMargin(now);
    }

    private void ProcessSubmit(in SimulationOrderCommand submit, Instant now)
    {
        if (TryProcessPackageSubmit(submit, now))
            return;

        if (submit.Execution.Algorithm == ExecutionAlgorithm.None)
            GetOrCreateInstrumentEngine(submit.Instrument).Submit(submit);
        else
            StartAlgorithmicOrder(submit, now);
    }

    private bool TryProcessPackageSubmit(in SimulationOrderCommand submit, Instant now)
    {
        if (!Account.TryGetContract(submit.Instrument, out var packageContract) ||
            packageContract.Package is null)
        {
            return false;
        }

        if (packageContract.Package.Kind != PackageKind.OptionSpread)
        {
            _executionEvents.Add(new OrderRejected(
                submit.ClientOrderId,
                submit.StrategyId,
                submit.VariantId,
                $"Package kind {packageContract.Package.Kind} does not have a simulation execution model.",
                submit.AssetId));
            return true;
        }

        if (_venueStatus != MarketStatus.Open)
        {
            _executionEvents.Add(new OrderRejected(
                submit.ClientOrderId,
                submit.StrategyId,
                submit.VariantId,
                $"Market is {_venueStatus}; replay order submission is disabled.",
                submit.AssetId));
            return true;
        }

        if (!TryBuildPackageLegFills(packageContract, submit, out var legFills, out var packagePrice, out var reason))
        {
            _executionEvents.Add(new OrderRejected(
                submit.ClientOrderId,
                submit.StrategyId,
                submit.VariantId,
                reason,
                submit.AssetId));
            return true;
        }

        if (!PackageLimitAllowsFill(submit, packagePrice))
        {
            _terminalOrderIds.Add(submit.ClientOrderId);
            _orderQuantities.Remove(submit.ClientOrderId);
            _orderFilledQuantities.Remove(submit.ClientOrderId);
            _executionEvents.Add(new OrderRejected(
                submit.ClientOrderId,
                submit.StrategyId,
                submit.VariantId,
                $"Package limit {submit.Execution.LimitPrice!.Value} is not marketable against atomic package price {packagePrice}.",
                submit.AssetId));
            return true;
        }

        if (!Account.TryReservePackage(
                submit,
                legFills,
                packagePrice,
                _config.Margin,
                _config.Settlement,
                SimulationPolicy.AllowCashBorrowing,
                out reason))
        {
            _executionEvents.Add(new OrderRejected(
                submit.ClientOrderId,
                submit.StrategyId,
                submit.VariantId,
                reason,
                submit.AssetId));
            return true;
        }

        var feeCurrency = packageContract.Exposure.SettlementCurrency();
        var thirtyDayVolume = Account.GetThirtyDayFeeVolume(submit.StrategyId, submit.VariantId, feeCurrency, now);
        var commission = _config.Fees.Calculate(packageContract, submit.Quantity, packagePrice, submit.Side, isMaker: false, thirtyDayVolume);
        Account.ApplyPackageFill(submit, legFills, packagePrice, commission, now);
        _terminalOrderIds.Add(submit.ClientOrderId);
        _orderQuantities.Remove(submit.ClientOrderId);
        _orderFilledQuantities.Remove(submit.ClientOrderId);
        _executionEvents.Add(new OrderAccepted(
            submit.ClientOrderId,
            submit.StrategyId,
            submit.VariantId,
            AssetId: submit.AssetId));
        _executionEvents.Add(new OrderFilled(
            submit.ClientOrderId,
            submit.Instrument,
            submit.VariantId,
            submit.StrategyId,
            submit.Side,
            submit.Quantity,
            packagePrice,
            commission,
            _identity.NextExecutionId(submit.Instrument),
            AssetId: submit.AssetId));
        for (var i = 0; i < legFills.Count; i++)
        {
            var leg = legFills[i];
            _executionEvents.Add(new PackageLegFilled(
                submit.ClientOrderId,
                submit.Instrument,
                leg.Instrument,
                submit.VariantId,
                submit.StrategyId,
                leg.Side,
                leg.Quantity,
                leg.Price,
                _identity.NextExecutionId(leg.Instrument),
                PackageAssetId: submit.AssetId));
        }

        _marginDirty = true;
        return true;
    }

    private bool TryBuildPackageLegFills(
        InstrumentContract packageContract,
        SimulationOrderCommand submit,
        out IReadOnlyList<PackageLegFill> legFills,
        out Price packagePrice,
        out string reason)
    {
        var fills = new List<PackageLegFill>(packageContract.Legs.Count);
        Currency currency = default;
        var netPackagePrice = 0m;
        for (var i = 0; i < packageContract.Legs.Count; i++)
        {
            var leg = packageContract.Legs[i];
            if (!Account.TryGetContract(leg.Instrument, out var legContract))
            {
                legFills = [];
                packagePrice = default;
                reason = $"Package leg {leg.Instrument} has no registered InstrumentContract.";
                return false;
            }

            if (!_lastMarks.TryGetValue(leg.Instrument, out var mark))
            {
                legFills = [];
                packagePrice = default;
                reason = $"Package leg {leg.Instrument} has no current mark for atomic execution.";
                return false;
            }

            if (currency == default)
                currency = mark.Currency;
            else if (mark.Currency != currency)
            {
                legFills = [];
                packagePrice = default;
                reason = $"Package leg {leg.Instrument} mark currency {mark.Currency} does not match package currency {currency}.";
                return false;
            }

            var legSide = submit.Side == Side.Buy ? leg.Side : Opposite(leg.Side);
            var quantity = new Qty(submit.Quantity.Value * Math.Abs(leg.Ratio));
            fills.Add(new PackageLegFill(leg.Instrument, legSide, quantity, mark));

            var sign = leg.Side == Side.Buy ? 1m : -1m;
            netPackagePrice += sign * Math.Abs(leg.Ratio) * mark.Value;
        }

        legFills = fills;
        packagePrice = new Price(submit.Side == Side.Buy ? netPackagePrice : -netPackagePrice, currency);
        reason = string.Empty;
        return true;
    }

    private static bool PackageLimitAllowsFill(SimulationOrderCommand submit, Price packagePrice)
    {
        if (submit.Execution.OrderType == OrderType.Market)
            return true;
        if (!submit.Execution.LimitPrice.HasValue)
            return false;

        var limit = submit.Execution.LimitPrice.Value;
        return submit.Side == Side.Buy
            ? packagePrice.Value <= limit.Value
            : packagePrice.Value >= limit.Value;
    }

    private static Side Opposite(Side side) => side == Side.Buy ? Side.Sell : Side.Buy;

    private void ProcessCancel(in SimulationCancelCommand cancel)
    {
        if (_terminalOrderIds.Contains(cancel.OrderId))
            return;

        if (!TryCancelActiveAlgoOrder(cancel))
            GetOrCreateInstrumentEngine(cancel.Instrument).Cancel(cancel);
    }

    private void ProcessModify(in SimulationModifyCommand modify)
    {
        if (_terminalOrderIds.Contains(modify.OrderId))
            return;

        if (IsActiveAlgoOrder(modify.OrderId))
        {
            _executionEvents.Add(new OrderRejected(
                modify.OrderId,
                modify.StrategyId,
                modify.VariantId,
                "Active algorithmic orders do not support modification.",
                modify.AssetId));
            return;
        }

        GetOrCreateInstrumentEngine(modify.Instrument).Modify(modify);
    }

    /// <summary>Cancel any active algorithmic orders which survived to replay end.</summary>
    public void CompleteReplay(Instant now, string reason = "Replay ended before algorithm completed.")
    {
        _currentTime = now;
        if (_activeAlgoOrders.Count == 0)
        {
            FlushPendingExecutionResponses();
            ProcessDueOptionLifecycle(now);
            Account.EmitPendingLifecycleStatuses(now);
            DrainAccountEvents();
            EmitOpenCustodySnapshots(now);
            return;
        }

        for (var i = _activeAlgoOrders.Count - 1; i >= 0; i--)
            CancelActiveAlgoOrder(_activeAlgoOrders[i], reason);

        _activeAlgoOrders.Clear();
        FlushPendingExecutionResponses();
        ProcessDueOptionLifecycle(now);
        Account.EmitPendingLifecycleStatuses(now);
        DrainAccountEvents();
        EmitOpenCustodySnapshots(now);
        ProcessMargin(now);
    }

    /// <summary>Apply a financing charge to the venue account.</summary>
    public AccountStatementSnapshot ApplyFinancing(FinancingChargeApplied financing, Instant now)
    {
        Account.ApplyFinancing(financing);
        return Account.CreateStatement(
            financing.StrategyId,
            financing.VariantId,
            financing.Amount.Currency,
            now,
            openOrders: CountOpenOrders());
    }

    public AccountStatementSnapshot CreateAccountStatement(StrategyId strategyId, int variantId, Currency currency, Instant now)
    {
        BuildStatementMarks(strategyId, variantId);
        return Account.CreateStatement(
            strategyId,
            variantId,
            currency,
            now,
            _statementMarks,
            openOrders: CountOpenOrders(strategyId, variantId));
    }

    private void EmitCorporateActionAccountSurfaces(int firstEffectIndex, int effectEndIndex, Instant now)
    {
        for (var i = firstEffectIndex; i < effectEndIndex; i++)
        {
            if (_simulationEvents[i] is not CorporateActionEffectSnapshot effect)
                continue;

            var currency = GetCorporateActionCurrency(effect);
            if (effect.ActionType == CorporateActionType.StockSplit)
            {
                _simulationEvents.Add(Account.CreateCustodySnapshot(
                    effect.StrategyId,
                    effect.VariantId,
                    effect.Instrument,
                    GetCorporateActionMark(effect, currency),
                    _config.AccountType,
                    _config.Margin,
                    now));
            }
            else if (effect.ActionType == CorporateActionType.CashDividend)
            {
                _simulationEvents.Add(CreatePerformanceSnapshot(effect.StrategyId, effect.VariantId, currency, now));
            }

            _simulationEvents.Add(CreateAccountStatement(effect.StrategyId, effect.VariantId, currency, now));
        }
    }

    private Currency GetCorporateActionCurrency(CorporateActionEffectSnapshot effect)
    {
        if (effect.CashAmount.HasValue)
            return effect.CashAmount.Value.Currency;
        if (effect.AvgEntryPriceAfter.Currency != default)
            return effect.AvgEntryPriceAfter.Currency;
        if (effect.AvgEntryPriceBefore.Currency != default)
            return effect.AvgEntryPriceBefore.Currency;
        return Account.Cash.Currency;
    }

    private Price GetCorporateActionMark(CorporateActionEffectSnapshot effect, Currency currency)
    {
        if (_engines.TryGetValue(effect.Instrument, out var engine))
        {
            var side = effect.QuantityAfter.Value >= 0m ? Side.Buy : Side.Sell;
            if (engine.TryGetPositionMarkPrice(side, out var mark))
                return mark.Currency == default ? new Price(mark.Value, currency) : mark;
        }

        var fallback = effect.AvgEntryPriceAfter.Currency == default
            ? new Price(effect.AvgEntryPriceAfter.Value, currency)
            : effect.AvgEntryPriceAfter;
        return fallback;
    }

    private void EmitOpenCustodySnapshots(Instant now)
    {
        Account.CopyPositions(_statementPositions);
        for (var i = 0; i < _statementPositions.Count; i++)
        {
            var position = _statementPositions[i];
            _simulationEvents.Add(Account.CreateCustodySnapshot(
                position.StrategyId,
                position.VariantId,
                position.Instrument,
                GetCustodyMark(position),
                _config.AccountType,
                _config.Margin,
                now));
        }
    }

    private Price GetCustodyMark(AccountPositionSnapshot position)
    {
        if (_engines.TryGetValue(position.Instrument, out var engine))
        {
            var side = position.Quantity.Value >= 0m ? Side.Buy : Side.Sell;
            if (engine.TryGetPositionMarkPrice(side, out var mark))
                return mark.Currency == default
                    ? new Price(mark.Value, Account.Cash.Currency)
                    : mark;
        }

        return position.AveragePrice.Currency == default
            ? new Price(position.AveragePrice.Value, Account.Cash.Currency)
            : position.AveragePrice;
    }

    private void EmitDeliveredCustodySnapshots(Instant now)
    {
        for (var i = 0; i < _pendingDeliveredCustodySnapshots.Count; i++)
        {
            var delivered = _pendingDeliveredCustodySnapshots[i];
            _simulationEvents.Add(Account.CreateCustodySnapshot(
                delivered.StrategyId,
                delivered.VariantId,
                delivered.Instrument,
                GetCustodyMark(new AccountPositionSnapshot(
                    delivered.StrategyId,
                    delivered.VariantId,
                    delivered.Instrument,
                    delivered.Quantity,
                    Price.Zero)),
                _config.AccountType,
                _config.Margin,
                now));
        }

        _pendingDeliveredCustodySnapshots.Clear();
    }

    public PerformanceSnapshot CreatePerformanceSnapshot(StrategyId strategyId, int variantId, Currency currency, Instant now)
    {
        var statement = CreateAccountStatement(strategyId, variantId, currency, now);
        return new PerformanceSnapshot(
            statement.Equity,
            statement.Cash,
            statement.UnrealizedPnL,
            statement.RealizedPnL,
            statement.OpenPositions,
            statement.OpenOrders)
        {
            Time = now
        };
    }

    /// <summary>Apply an account transfer to the venue account and emit status events.</summary>
    internal int ApplyAccountTransfer(AccountTransferCompleted transfer, Instant now, List<AccountStatementSnapshot> statements)
    {
        var initialStatementCount = statements.Count;
        if (!Account.TryApplyAccountTransfer(transfer, out var reason))
        {
            _simulationEvents.Add(new AccountTransferFailed(
                transfer.TransferId,
                transfer.StrategyId,
                transfer.VariantId,
                transfer.TransferType,
                transfer.CashAmount,
                transfer.Instrument,
                transfer.Quantity,
                now,
                reason,
                transfer.ExternalReference,
                transfer.DestinationStrategyId,
                transfer.DestinationVariantId,
                transfer.Venue,
                transfer.CarryingPrice)
            {
                Time = now
            });
            _simulationEvents.Add(new AccountTransferStatusSnapshot(
                transfer.TransferId,
                transfer.StrategyId,
                transfer.VariantId,
                transfer.TransferType,
                AccountTransferStatus.Failed,
                transfer.CashAmount,
                transfer.Instrument,
                transfer.Quantity,
                now,
                reason,
                transfer.ExternalReference,
                transfer.DestinationStrategyId,
                transfer.DestinationVariantId,
                transfer.Venue,
                transfer.CarryingPrice)
            {
                Time = now
            });
            return 0;
        }

        _simulationEvents.Add(new AccountTransferStatusSnapshot(
            transfer.TransferId,
            transfer.StrategyId,
            transfer.VariantId,
            transfer.TransferType,
            AccountTransferStatus.Completed,
            transfer.CashAmount,
            transfer.Instrument,
            transfer.Quantity,
            now,
            null,
            transfer.ExternalReference,
            transfer.DestinationStrategyId,
            transfer.DestinationVariantId,
            transfer.Venue,
            transfer.CarryingPrice)
        {
            Time = now
        });

        EmitAccountTransferCustodySnapshots(transfer, now);

        statements.Add(
            Account.CreateStatement(
                transfer.StrategyId,
                transfer.VariantId,
                Account.Cash.Currency,
                now,
                openOrders: CountOpenOrders()));

        if (transfer.TransferType == AccountTransferType.InternalTransfer
            && transfer.DestinationStrategyId.HasValue)
        {
            statements.Add(Account.CreateStatement(
                transfer.DestinationStrategyId.Value,
                transfer.DestinationVariantId,
                Account.Cash.Currency,
                now,
                openOrders: CountOpenOrders()));
        }

        _marginDirty = true;
        ProcessMargin(now);
        return statements.Count - initialStatementCount;
    }

    private void EmitAccountTransferCustodySnapshots(AccountTransferCompleted transfer, Instant now)
    {
        if (transfer.Instrument is not { } instrument
            || transfer.CarryingPrice is not { } carryingPrice)
        {
            return;
        }

        _simulationEvents.Add(Account.CreateCustodySnapshot(
            transfer.StrategyId,
            transfer.VariantId,
            instrument,
            carryingPrice,
            _config.AccountType,
            _config.Margin,
            now));

        if (transfer.TransferType == AccountTransferType.InternalTransfer
            && transfer.DestinationStrategyId.HasValue)
        {
            _simulationEvents.Add(Account.CreateCustodySnapshot(
                transfer.DestinationStrategyId.Value,
                transfer.DestinationVariantId,
                instrument,
                carryingPrice,
                _config.AccountType,
                _config.Margin,
                now));
        }
    }

    /// <summary>Drain pending execution events into a caller-owned buffer.</summary>
    public int DrainExecutionEvents(Span<ExecutionEvent> destination)
    {
        DrainEngineEvents();
        if (_drainingFlushedExecutionResponses)
        {
            var flushedCount = Math.Min(destination.Length, _executionEvents.Count);
            for (var i = 0; i < flushedCount; i++)
            {
                destination[i] = _executionEvents[i];
                TrackOrderStateSnapshot(_executionEvents[i]);
            }

            _executionEvents.RemoveRange(0, flushedCount);
            if (_executionEvents.Count == 0)
                _drainingFlushedExecutionResponses = false;

            return flushedCount;
        }

        QueueExecutionResponses();
        var count = Math.Min(destination.Length, _executionEvents.Count);
        if (_config.Latency.ResponseMean <= Duration.Zero)
        {
            for (var i = 0; i < count; i++)
            {
                var visibleEvent = WithTime(_executionEvents[i], _currentTime);
                destination[i] = visibleEvent;
                TrackOrderStateSnapshot(visibleEvent);
            }

            _executionEvents.RemoveRange(0, count);
            return count;
        }

        count = 0;
        for (var i = 0; i < _pendingExecutionResponses.Count && count < destination.Length; i++)
        {
            var pending = _pendingExecutionResponses[i];
            if (pending.VisibleAt > _currentTime)
                continue;

            destination[count++] = pending.Event;
            TrackOrderStateSnapshot(pending.Event);
            _pendingExecutionResponses.RemoveAt(i);
            i--;
        }

        return count;
    }

    /// <summary>Drain pending non-execution simulator events into a caller-owned buffer.</summary>
    public int DrainSimulationEvents(Span<FinanceEvent> destination)
    {
        DrainAccountEvents();
        var count = Math.Min(destination.Length, _simulationEvents.Count);
        for (var i = 0; i < count; i++)
            destination[i] = _simulationEvents[i];

        _simulationEvents.RemoveRange(0, count);
        return count;
    }

    /// <summary>Emit account lifecycle statuses that are due at the supplied time.</summary>
    public void EmitPendingAccountLifecycleStatuses(Instant now)
    {
        Account.EmitPendingLifecycleStatuses(now);
        DrainAccountEvents();
    }

    private void DrainEngineEvents()
    {
        foreach (var engine in _engines.Values)
        {
            while (true)
            {
                var count = engine.DrainExecutionEvents(_engineBuffer);
                if (count == 0)
                    break;

                for (var i = 0; i < count; i++)
                    _executionEvents.Add(_engineBuffer[i]);
            }
        }
    }

    private void QueueExecutionResponses()
    {
        if (_config.Latency.ResponseMean <= Duration.Zero || _executionEvents.Count == 0)
            return;

        var visibleAt = _currentTime + _config.Latency.ResponseMean;
        for (var i = 0; i < _executionEvents.Count; i++)
        {
            _pendingExecutionResponses.Add(new PendingExecutionResponse(
                WithTime(_executionEvents[i], visibleAt),
                visibleAt,
                ++_nextResponseSequence));
        }

        _executionEvents.Clear();
        _pendingExecutionResponses.Sort(static (left, right) =>
        {
            var timeComparison = left.VisibleAt.CompareTo(right.VisibleAt);
            return timeComparison != 0
                ? timeComparison
                : left.Sequence.CompareTo(right.Sequence);
        });
    }

    private static ExecutionEvent WithTime(ExecutionEvent evt, Instant time)
        => evt with { Time = time };

    private void TrackOrderStateSnapshot(ExecutionEvent evt)
    {
        if (!TryCreateOrderStateSnapshot(evt, out var snapshot))
            return;

        _simulationEvents.Add(snapshot with { Time = evt.Time });
    }

    private bool TryCreateOrderStateSnapshot(ExecutionEvent evt, out OrderStateSnapshot snapshot)
    {
        snapshot = evt switch
        {
            OrderAccepted accepted => TrackAcceptedOrderState(accepted),
            OrderModified modified => TrackModifiedOrderState(modified),
            OrderRejected rejected => TrackRejectedOrderState(rejected),
            OrderCancelled cancelled => TrackCancelledOrderState(cancelled),
            OrderExpired expired => TrackExpiredOrderState(expired),
            OrderFilled filled => TrackFilledOrderState(filled),
            _ => null!
        };

        return snapshot is not null;
    }

    private OrderStateSnapshot TrackAcceptedOrderState(OrderAccepted accepted)
    {
        _terminalOrderIds.Remove(accepted.OrderId);
        _orderFilledQuantities.TryAdd(accepted.OrderId, Qty.Zero);
        return new OrderStateSnapshot(
            accepted.OrderId,
            accepted.StrategyId,
            accepted.VariantId,
            OrderStatus.Open);
    }

    private OrderStateSnapshot TrackModifiedOrderState(OrderModified modified)
    {
        if (modified.NewQuantity.HasValue)
            _orderQuantities[modified.OrderId] = modified.NewQuantity.Value;

        return new OrderStateSnapshot(
            modified.OrderId,
            modified.StrategyId,
            modified.VariantId,
            OrderStatus.Open,
            RemainingQty: modified.NewQuantity);
    }

    private OrderStateSnapshot TrackRejectedOrderState(OrderRejected rejected)
    {
        _terminalOrderIds.Add(rejected.OrderId);
        _orderQuantities.Remove(rejected.OrderId);
        _orderFilledQuantities.Remove(rejected.OrderId);
        return new OrderStateSnapshot(
            rejected.OrderId,
            rejected.StrategyId,
            rejected.VariantId,
            OrderStatus.Rejected,
            Reason: rejected.Reason);
    }

    private OrderStateSnapshot TrackCancelledOrderState(OrderCancelled cancelled)
    {
        _terminalOrderIds.Add(cancelled.OrderId);
        _orderQuantities.Remove(cancelled.OrderId);
        _orderFilledQuantities.Remove(cancelled.OrderId);
        return new OrderStateSnapshot(
            cancelled.OrderId,
            cancelled.StrategyId,
            cancelled.VariantId,
            OrderStatus.Cancelled,
            RemainingQty: cancelled.RemainingQty,
            Reason: cancelled.Reason);
    }

    private OrderStateSnapshot TrackExpiredOrderState(OrderExpired expired)
    {
        _terminalOrderIds.Add(expired.OrderId);
        _orderQuantities.Remove(expired.OrderId);
        _orderFilledQuantities.Remove(expired.OrderId);
        return new OrderStateSnapshot(
            expired.OrderId,
            expired.StrategyId,
            expired.VariantId,
            OrderStatus.Expired);
    }

    private OrderStateSnapshot TrackFilledOrderState(OrderFilled filled)
    {
        var previousFilled = _orderFilledQuantities.GetValueOrDefault(filled.OrderId, Qty.Zero);
        var cumulativeFilled = previousFilled + filled.FilledQty;
        var remainingQuantity = Qty.Zero;
        if (_orderQuantities.TryGetValue(filled.OrderId, out var orderQuantity))
            remainingQuantity = new Qty(Math.Max(0m, orderQuantity.Value - cumulativeFilled.Value));

        var status = remainingQuantity.Value <= 0m
            ? OrderStatus.Filled
            : OrderStatus.PartiallyFilled;

        if (status == OrderStatus.Filled)
        {
            _terminalOrderIds.Add(filled.OrderId);
            _orderQuantities.Remove(filled.OrderId);
            _orderFilledQuantities.Remove(filled.OrderId);
        }
        else
        {
            _orderFilledQuantities[filled.OrderId] = cumulativeFilled;
        }

        return new OrderStateSnapshot(
            filled.OrderId,
            filled.StrategyId,
            filled.VariantId,
            status,
            FilledQty: cumulativeFilled,
            RemainingQty: remainingQuantity);
    }

    private void FlushPendingExecutionResponses()
    {
        QueueExecutionResponses();
        if (_pendingExecutionResponses.Count == 0)
            return;

        for (var i = 0; i < _pendingExecutionResponses.Count; i++)
            _executionEvents.Add(_pendingExecutionResponses[i].Event);

        _pendingExecutionResponses.Clear();
        _drainingFlushedExecutionResponses = true;
    }

    private void DrainAccountEvents()
    {
        while (true)
        {
            var count = Account.DrainEvents(_accountEventBuffer);
            if (count == 0)
                return;

            for (var i = 0; i < count; i++)
            {
                _simulationEvents.Add(_accountEventBuffer[i]);
                if (_accountEventBuffer[i] is AssetDeliveryScheduled scheduled)
                {
                    _simulationEvents.Add(Account.CreateCustodySnapshot(
                        scheduled.StrategyId,
                        scheduled.VariantId,
                        scheduled.Instrument,
                        GetCustodyMark(new AccountPositionSnapshot(
                            scheduled.StrategyId,
                            scheduled.VariantId,
                            scheduled.Instrument,
                            scheduled.Quantity,
                            Price.Zero)),
                        _config.AccountType,
                        _config.Margin,
                        scheduled.Time));
                }
                else if (_accountEventBuffer[i] is AssetDelivered delivered)
                {
                    _pendingDeliveredCustodySnapshots.Add(delivered);
                }
            }
        }
    }

    private void ExpireDueOrders(Instant now)
    {
        foreach (var engine in _engines.Values)
            engine.ExpireDueOrders(now);
    }

    private bool AllowsExecution(FinanceEvent evt)
        => evt switch
        {
            BarClosed => SimulationPolicy.BarExecution,
            TradeOccurred => SimulationPolicy.TradeExecution,
            _ => true
        };

    private void ProcessMargin(Instant now)
    {
        if (_config.AccountType != AccountType.Margin)
            return;
        if (!_marginDirty)
            return;

        _marginDirty = false;

        BuildMarginMarks();

        Account.CalculateMarginStatuses(
            _marginMarks,
            _lastMarks,
            _config.Margin,
            Account.Cash.Currency,
            _marginStatuses,
            _marginStatusAccumulators);
        _emittedMarginKeys.Clear();
        foreach (var status in _marginStatuses)
        {
            var key = (status.StrategyId, status.VariantId, status.Equity.Currency);
            _emittedMarginKeys.Add(key);
            _simulationEvents.Add(new MarginStatusSnapshot(
                status.StrategyId,
                status.VariantId,
                status.Equity,
                status.MaintenanceRequirement,
                status.IsMaintenanceBreached)
            {
                Time = now
            });

            if (status.IsMaintenanceBreached)
                ProcessMarginBreach(key, status, now, _marginMarks);
            else if (_activeMarginCalls.Remove(key))
                _simulationEvents.Add(new MarginCallResolved(
                    status.StrategyId,
                    status.VariantId,
                    status.Equity,
                    status.MaintenanceRequirement)
                {
                    Time = now
                });
        }

        _resolvedMarginKeys.Clear();
        foreach (var key in _activeMarginCalls.Keys)
        {
            if (!_emittedMarginKeys.Contains(key))
                _resolvedMarginKeys.Add(key);
        }

        foreach (var key in _resolvedMarginKeys)
        {
            _activeMarginCalls.Remove(key);
            _simulationEvents.Add(new MarginCallResolved(
                key.StrategyId,
                key.VariantId,
                Account.Cash,
                Money.Zero(key.Currency))
            {
                Time = now
            });
        }
        _resolvedMarginKeys.Clear();
        _marginStatuses.Clear();
    }

    private void StartAlgorithmicOrder(SimulationOrderCommand command, Instant now)
    {
        var algorithm = command.Execution.Algorithm;
        if (algorithm is not (ExecutionAlgorithm.Twap or ExecutionAlgorithm.Vwap or ExecutionAlgorithm.Pov))
        {
            _executionEvents.Add(new OrderRejected(
                command.ClientOrderId,
                command.StrategyId,
                command.VariantId,
                $"Simulated venue does not support execution algorithm {algorithm}.",
                command.AssetId));
            return;
        }

        var hasHorizon = command.Execution.Horizon > Duration.Zero;
        if (!hasHorizon && algorithm != ExecutionAlgorithm.Pov)
        {
            _executionEvents.Add(new OrderRejected(
                command.ClientOrderId,
                command.StrategyId,
                command.VariantId,
                $"{algorithm.ToString().ToUpperInvariant()} requires a positive horizon_secs parameter.",
                command.AssetId));
            return;
        }

        if (algorithm == ExecutionAlgorithm.Pov && command.Execution.ParticipationRate <= 0m)
        {
            _executionEvents.Add(new OrderRejected(
                command.ClientOrderId,
                command.StrategyId,
                command.VariantId,
                "POV requires a positive participation_rate parameter.",
                command.AssetId));
            return;
        }

        var interval = command.Execution.Interval > Duration.Zero
            ? command.Execution.Interval
            : algorithm is ExecutionAlgorithm.Vwap or ExecutionAlgorithm.Pov
                ? Duration.Zero
                : command.Execution.Horizon;
        if (algorithm == ExecutionAlgorithm.Twap && interval <= Duration.Zero)
            interval = command.Execution.Horizon;

        _executionEvents.Add(new OrderAccepted(
            command.ClientOrderId,
            command.StrategyId,
            command.VariantId,
            AssetId: command.AssetId));
        _activeAlgoOrders.Add(new ActiveAlgoOrder
        {
            Command = command,
            Algorithm = algorithm,
            RemainingQuantity = command.Quantity,
            StartedAt = now,
            EndsAt = hasHorizon ? now + command.Execution.Horizon : Instant.MaxValue,
            Interval = interval,
            NextSliceAt = interval > Duration.Zero ? now + interval : now,
            ParticipationRate = command.Execution.ParticipationRate > 0m
                ? command.Execution.ParticipationRate
                : 1m,
            ForceCompleteAtHorizon = algorithm is ExecutionAlgorithm.Twap or ExecutionAlgorithm.Vwap
        });
    }

    private void ProcessActiveAlgoOrders(Instant now, FinanceEvent? evt)
    {
        for (var i = _activeAlgoOrders.Count - 1; i >= 0; i--)
        {
            var algo = _activeAlgoOrders[i];
            if (algo.RemainingQuantity.Value <= 0m)
            {
                _activeAlgoOrders.RemoveAt(i);
                continue;
            }

            switch (algo.Algorithm)
            {
                case ExecutionAlgorithm.Twap:
                    ProcessTwapSlice(algo, now);
                    break;
                case ExecutionAlgorithm.Vwap:
                    ProcessVwapSlice(algo, now, evt);
                    break;
                case ExecutionAlgorithm.Pov:
                    ProcessPovSlice(algo, now, evt);
                    break;
            }

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
            var remainingIntervals = Math.Max(
                1,
                (int)Math.Ceiling((algo.EndsAt - algo.NextSliceAt).TotalSeconds / Math.Max(1e-9, algo.Interval.TotalSeconds)) + 1);
            var sliceQty = new Qty(Math.Min(
                algo.RemainingQuantity.Value,
                Math.Ceiling(algo.RemainingQuantity.Value / remainingIntervals * 1_000_000m) / 1_000_000m));
            SubmitAlgoMarketSlice(algo, sliceQty);
            algo.NextSliceAt += algo.Interval;
        }
    }

    private void ProcessVwapSlice(ActiveAlgoOrder algo, Instant now, FinanceEvent? evt)
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

    private void ProcessPovSlice(ActiveAlgoOrder algo, Instant now, FinanceEvent? evt)
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
            Execution = new ExecutionSpec(
                OrderType.Market,
                timeInForce: algo.Command.Execution.TimeInForce,
                maxSlippageTicks: algo.Command.Execution.MaxSlippageTicks),
            OrderListId = null,
            ContingencyType = null
        };

        GetOrCreateInstrumentEngine(slice.Instrument).Submit(slice);
        algo.RemainingQuantity = new Qty(Math.Max(0m, algo.RemainingQuantity.Value - quantity.Value));
    }

    private bool TryCancelActiveAlgoOrder(SimulationCancelCommand command)
    {
        for (var i = 0; i < _activeAlgoOrders.Count; i++)
        {
            var algo = _activeAlgoOrders[i];
            if (algo.Command.ClientOrderId != command.OrderId)
                continue;

            _activeAlgoOrders.RemoveAt(i);
            CancelActiveAlgoOrder(algo, command.Reason ?? "Cancelled active algorithmic order.");
            return true;
        }

        return false;
    }

    private bool IsActiveAlgoOrder(OrderId orderId)
    {
        for (var i = 0; i < _activeAlgoOrders.Count; i++)
        {
            if (_activeAlgoOrders[i].Command.ClientOrderId == orderId)
                return true;
        }

        return false;
    }

    private void CancelActiveAlgoOrder(ActiveAlgoOrder algo, string reason)
    {
        if (algo.RemainingQuantity.Value <= 0m)
            return;

        _executionEvents.Add(new OrderCancelled(
            algo.Command.ClientOrderId,
            algo.Command.StrategyId,
            algo.Command.VariantId,
            algo.RemainingQuantity,
            reason,
            AssetId: algo.Command.AssetId));
        algo.RemainingQuantity = Qty.Zero;
    }

    private static decimal GetEventVolume(FinanceEvent? evt)
        => evt switch
        {
            TradeOccurred trade => trade.Trade.Size.Value,
            BarClosed bar => bar.Bar.Volume.Value,
            QuoteReceived quote => quote.Quote.BidSize.Value + quote.Quote.AskSize.Value,
            BookSnapshotReceived book => SumLevels(book.Book.Bids) + SumLevels(book.Book.Asks),
            BookLevelDeltaReceived delta => delta.Delta.Size.Value,
            BookLevelDeltasReceived deltas => SumDeltas(deltas.Deltas),
            BookOrderAdded added => added.Order.Size.Value,
            BookOrderModified modified => modified.Order.Size.Value,
            BookOrderExecuted executed => executed.ExecutedSize.Value,
            BookDepthSnapshotReceived depth => SumLevels(depth.Bids) + SumLevels(depth.Asks),
            BookDepth10Received depth => SumLevels(depth.Bids, 10) + SumLevels(depth.Asks, 10),
            _ => 0m
        };

    private static decimal SumLevels(IReadOnlyList<Level> levels, int maxDepth = int.MaxValue)
    {
        var count = levels.Count < maxDepth ? levels.Count : maxDepth;
        var sum = 0m;
        for (var i = 0; i < count; i++)
        {
            sum += levels[i].Size.Value;
        }

        return sum;
    }

    private static decimal SumDeltas(IReadOnlyList<BookLevelDelta> deltas)
    {
        var sum = 0m;
        for (var i = 0; i < deltas.Count; i++)
        {
            sum += deltas[i].Size.Value;
        }

        return sum;
    }

    private void ProcessMarginBreach(
        (StrategyId StrategyId, int VariantId, Currency Currency) key,
        MarginAccountStatus status,
        Instant now,
        IReadOnlyDictionary<(StrategyId StrategyId, int VariantId, Instrument Instrument), Price> marks)
    {
        if (!_activeMarginCalls.TryGetValue(key, out var call))
        {
            call = new ActiveMarginCall(now + _config.Margin.MarginCallGracePeriod);
            _activeMarginCalls[key] = call;
            _simulationEvents.Add(new MarginCallIssued(
                status.StrategyId,
                status.VariantId,
                status.Equity,
                status.MaintenanceRequirement,
                call.DueAt)
            {
                Time = now
            });
        }

        if (now < call.DueAt)
            return;

        _activeMarginCalls.Remove(key);
        _simulationEvents.Add(new RiskLimitBreached(
            $"MaintenanceMargin:{key.StrategyId.Value}:{key.VariantId}",
            status.Equity.Amount,
            status.MaintenanceRequirement.Amount)
        {
            Time = now
        });

        foreach (var engine in _engines.Values)
            engine.CancelOpenOrdersForMargin(key.StrategyId, key.VariantId);

        if (_config.Margin.LiquidationPolicy == LiquidationPolicy.CancelOpenOrdersOnly)
        {
            DrainEngineEvents();
            return;
        }

        Account.CopyPositions(key.StrategyId, key.VariantId, _liquidationPositions);
        while (_liquidationPositions.Count > 0)
        {
            var selectedIndex = 0;
            var selectedRequirement = GetMaintenanceRequirement(_liquidationPositions[0], marks).Amount;
            for (var i = 1; i < _liquidationPositions.Count; i++)
            {
                var requirement = GetMaintenanceRequirement(_liquidationPositions[i], marks).Amount;
                if (requirement > selectedRequirement)
                {
                    selectedIndex = i;
                    selectedRequirement = requirement;
                }
            }

            var position = _liquidationPositions[selectedIndex];
            _liquidationPositions.RemoveAt(selectedIndex);
            if (!_engines.TryGetValue(position.Instrument, out var engine))
                continue;

            var mark = marks.TryGetValue((position.StrategyId, position.VariantId, position.Instrument), out var marked)
                ? marked
                : position.AveragePrice;
            var quantity = position.Quantity.Abs;
            if (_config.Margin.LiquidationPolicy == LiquidationPolicy.CancelOpenOrdersAndReduceToMaintenance)
            {
                quantity = GetRequiredMaintenanceLiquidationQuantity(status, position, mark);
                if (quantity.Value <= 0m)
                    break;
            }

            engine.Liquidate(position, mark, quantity);
        }

        DrainEngineEvents();
    }

    private Qty GetRequiredMaintenanceLiquidationQuantity(
        MarginAccountStatus status,
        AccountPositionSnapshot position,
        Price mark)
    {
        var deficit = status.MaintenanceRequirement.Amount - status.Equity.Amount;
        if (deficit <= 0m)
            return Qty.Zero;

        var availableQuantity = position.Quantity.Abs;
        if (availableQuantity.Value <= 0m)
            return Qty.Zero;

        var positionRequirement = Account.CalculateMaintenanceRequirement(position, mark, _config.Margin, _lastMarks).Amount;
        var maintenancePerUnit = positionRequirement / availableQuantity.Value;
        if (maintenancePerUnit <= 0m)
            return availableQuantity;

        var quantity = deficit / maintenancePerUnit;
        if (quantity <= 0m)
            return Qty.Zero;

        return new Qty(Math.Min(availableQuantity.Value, quantity));
    }

    private Money GetMaintenanceRequirement(
        AccountPositionSnapshot position,
        IReadOnlyDictionary<(StrategyId StrategyId, int VariantId, Instrument Instrument), Price> marks)
    {
        var mark = marks.TryGetValue((position.StrategyId, position.VariantId, position.Instrument), out var marked)
            ? marked
            : position.AveragePrice;
        return Account.CalculateMaintenanceRequirement(position, mark, _config.Margin, _lastMarks);
    }

    private void BuildMarginMarks()
    {
        _marginMarks.Clear();
        Account.CopyPositions(_statementPositions);
        for (var i = 0; i < _statementPositions.Count; i++)
        {
            var position = _statementPositions[i];
            if (!_engines.TryGetValue(position.Instrument, out var engine))
                continue;

            var positionSide = position.Quantity.Value >= 0m ? Side.Buy : Side.Sell;
            if (engine.TryGetPositionMarkPrice(positionSide, out var mark))
            {
                _marginMarks[(position.StrategyId, position.VariantId, position.Instrument)] = mark;
            }
            else if (_lastMarks.TryGetValue(position.Instrument, out var lastMark))
            {
                _marginMarks[(position.StrategyId, position.VariantId, position.Instrument)] = lastMark;
            }
        }
    }

    private int CountOpenOrders()
    {
        var count = 0;
        foreach (var engine in _engines.Values)
        {
            count += engine.OpenOrders;
        }

        return count;
    }

    private int CountOpenOrders(StrategyId strategyId, int variantId)
    {
        var count = 0;
        foreach (var engine in _engines.Values)
            count += engine.CountOpenOrders(strategyId, variantId);

        return count;
    }

    private void BuildStatementMarks(StrategyId strategyId, int variantId)
    {
        _statementMarks.Clear();
        Account.CopyPositions(strategyId, variantId, _statementPositions);
        for (var i = 0; i < _statementPositions.Count; i++)
        {
            var position = _statementPositions[i];
            if (!_engines.TryGetValue(position.Instrument, out var engine))
                continue;

            var positionSide = position.Quantity.Value >= 0m ? Side.Buy : Side.Sell;
            if (engine.TryGetPositionMarkPrice(positionSide, out var mark))
                _statementMarks[position.Instrument] = mark;
            else if (_lastMarks.TryGetValue(position.Instrument, out var lastMark))
                _statementMarks[position.Instrument] = lastMark;
        }
    }

    private bool ProcessDueOptionLifecycle(Instant now)
    {
        var applied = false;
        _lifecycleScheduler.CopyDue(now, _dueLifecycleWork);
        for (var i = 0; i < _dueLifecycleWork.Count; i++)
        {
            var due = _dueLifecycleWork[i];
            var contract = Account.ResolveContract(due.Instrument);

            if (contract.Payoff is not PayoffTerms.Option option)
                continue;

            Account.CopyPositions(due.Instrument, _lifecyclePositions);
            if (_lifecyclePositions.Count == 0)
            {
                _lifecycleScheduler.MarkCompleted(due.Instrument);
                continue;
            }

            var allPositionsApplied = true;
            for (var positionIndex = 0; positionIndex < _lifecyclePositions.Count; positionIndex++)
            {
                var position = _lifecyclePositions[positionIndex];
                var reference = ResolveOptionLifecycleReference(position, contract, option.Terms, due.Expiry);
                if (reference.Price is null)
                {
                    allPositionsApplied = false;
                    var key = (position.StrategyId, position.VariantId, position.Instrument, due.Expiry);
                    if (!_blockedLifecycleNotices.Contains(key))
                    {
                        _blockedLifecycleNotices.Add(key);
                        var blockedResult = _optionLifecycleProcessor.Process(new OptionLifecycleRequest(
                            contract,
                            position.Quantity,
                            reference,
                            now));
                        Account.ApplyOptionLifecycleResult(
                            position.StrategyId,
                            position.VariantId,
                            position.Instrument,
                            blockedResult);
                    }

                    continue;
                }

                var assignmentInput = position.Quantity.Value < 0m &&
                    TryGetAssignmentInput(position, out var resolvedAssignmentInput)
                    ? resolvedAssignmentInput
                    : null;
                var result = _optionLifecycleProcessor.Process(new OptionLifecycleRequest(
                    contract,
                    position.Quantity,
                    reference,
                    now,
                    assignmentInput));
                var status = Account.ApplyOptionLifecycleResult(
                    position.StrategyId,
                    position.VariantId,
                    position.Instrument,
                    result);
                var positionApplied = status == OptionLifecycleApplicationStatus.Completed;
                applied |= positionApplied;
                allPositionsApplied &= positionApplied || status == OptionLifecycleApplicationStatus.NoOpenPosition;
                if (positionApplied)
                    _blockedLifecycleNotices.Remove((position.StrategyId, position.VariantId, position.Instrument, due.Expiry));
            }

            if (allPositionsApplied)
                _lifecycleScheduler.MarkCompleted(due.Instrument);
            else
                _lifecycleScheduler.MarkPending(due.Instrument);
        }

        if (applied)
            _marginDirty = true;

        return applied;
    }

    private bool TryGetAssignmentInput(AccountPositionSnapshot position, out SimulationOptionAssignmentInput assignmentInput)
    {
        var key = new SimulationOptionAssignmentKey(position.StrategyId, position.VariantId, position.Instrument);
        if (_assignmentInputs.TryGetValue(key, out assignmentInput!))
            return true;

        return _config.Lifecycle.TryGetAssignmentInput(
            position.StrategyId,
            position.VariantId,
            position.Instrument,
            out assignmentInput);
    }

    private OptionLifecycleReference ResolveOptionLifecycleReference(
        AccountPositionSnapshot position,
        InstrumentContract contract,
        OptionTerms terms,
        Instant expiry)
    {
        if (_settlementReferencePrices.TryGetValue(contract.Instrument, out var referencePrice))
            return new OptionLifecycleReference(referencePrice, OptionLifecycleReferenceSource.InstrumentSettlementData);

        if (_settlementReferencePrices.TryGetValue(terms.Underlying, out referencePrice))
            return new OptionLifecycleReference(referencePrice, OptionLifecycleReferenceSource.UnderlyingSettlementData);

        if (_config.Lifecycle.SettlementReferencePrices.TryGetValue(contract.Instrument, out referencePrice))
            return new OptionLifecycleReference(referencePrice, OptionLifecycleReferenceSource.InstrumentSettlementOverride);

        if (_config.Lifecycle.SettlementReferencePrices.TryGetValue(terms.Underlying, out referencePrice))
            return new OptionLifecycleReference(referencePrice, OptionLifecycleReferenceSource.UnderlyingSettlementOverride);

        if (_lastMarks.TryGetValue(terms.Underlying, out referencePrice))
            return new OptionLifecycleReference(referencePrice, OptionLifecycleReferenceSource.MarketMark);

        var reason = $"Cannot apply lifecycle for {position.Instrument}: no settlement/reference price for {terms.Underlying} at expiry {expiry}.";
        if (_config.Lifecycle.MissingReferencePricePolicy == MissingReferencePricePolicy.Throw)
            throw new InvalidOperationException(reason);

        return new OptionLifecycleReference(null, OptionLifecycleReferenceSource.None, reason);
    }

    private bool HasOptionLifecycleReference(InstrumentContract contract, OptionTerms terms) =>
        _settlementReferencePrices.ContainsKey(contract.Instrument)
        || _settlementReferencePrices.ContainsKey(terms.Underlying)
        || _config.Lifecycle.SettlementReferencePrices.ContainsKey(contract.Instrument)
        || _config.Lifecycle.SettlementReferencePrices.ContainsKey(terms.Underlying)
        || _lastMarks.ContainsKey(terms.Underlying)
        || _config.Lifecycle.MissingReferencePricePolicy == MissingReferencePricePolicy.Throw;

    private static bool TryGetMarketMark(MarketEvent evt, out Price mark)
    {
        switch (evt)
        {
            case QuoteReceived quote:
                mark = quote.Quote.Mid;
                return true;
            case TradeOccurred trade:
                mark = trade.Trade.Price;
                return true;
            case BarClosed bar:
                mark = bar.Bar.Close;
                return true;
            default:
                mark = default;
                return false;
        }
    }

    private void RecordEntryLatency(Instant submittedAt, Instant arrivesAt)
        => RecordEntryLatency(arrivesAt - submittedAt);

    private void RecordEntryLatency(Duration latency)
    {
        LatencySampleCount++;
        _totalEntryLatencyNanos += latency.Nanos;
        _minEntryLatencyNanos = Math.Min(_minEntryLatencyNanos, latency.Nanos);
        _maxEntryLatencyNanos = Math.Max(_maxEntryLatencyNanos, latency.Nanos);
    }

    private static Instant GetEventTime(FinanceEvent evt)
        => evt switch
        {
            QuoteReceived quote => quote.Quote.Time.ExchangeTime,
            TradeOccurred trade => trade.Trade.Time.ExchangeTime,
            BarClosed bar => bar.Bar.Time,
            BookSnapshotReceived book => book.Book.Time,
            BookDepth10Received depth => depth.Time,
            SettlementReferencePricePublished settlement => settlement.EffectiveAt,
            OptionAssignmentNoticePublished assignment => assignment.EffectiveAt,
            _ => evt.Time
        };

    private bool TryGetLifecycleMarketStatus(FinanceEvent evt, out MarketStatus status)
    {
        switch (evt)
        {
            case MarketOpened opened when opened.Venue == Venue:
                status = MarketStatus.Open;
                return true;
            case MarketClosed closed when closed.Venue == Venue:
                status = MarketStatus.Closed;
                return true;
            case PreMarketOpened preMarket when preMarket.Venue == Venue:
                status = MarketStatus.PreOpen;
                return true;
            case PostMarketOpened postMarket when postMarket.Venue == Venue:
                status = MarketStatus.Closed;
                return true;
            default:
                status = default;
                return false;
        }
    }

    private readonly record struct ActiveMarginCall(Instant DueAt);

    private readonly record struct PendingExecutionResponse(
        ExecutionEvent Event,
        Instant VisibleAt,
        long Sequence);

    private sealed class ActiveAlgoOrder
    {
        public required SimulationOrderCommand Command { get; init; }
        public required ExecutionAlgorithm Algorithm { get; init; }
        public required Qty RemainingQuantity { get; set; }
        public required Instant StartedAt { get; init; }
        public required Instant EndsAt { get; init; }
        public required Duration Interval { get; init; }
        public required Instant NextSliceAt { get; set; }
        public required decimal ParticipationRate { get; init; }
        public required bool ForceCompleteAtHorizon { get; init; }
    }
}
