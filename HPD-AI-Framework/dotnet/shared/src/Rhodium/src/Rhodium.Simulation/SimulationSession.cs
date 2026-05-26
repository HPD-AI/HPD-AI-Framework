using HPD.Events;
using HPD.Events.Core;
using Rhodium.Analytics;
using Rhodium.Control;
using Rhodium.Events;
using Rhodium.Kernel;
using Rhodium.Platform;
using Rhodium.Platform.Patterns;
using Rhodium.Primitives;
using Rhodium.Simulation.Data;
using Rhodium.Simulation.Diagnostics;
using Rhodium.Simulation.Exchange;
using Rhodium.Simulation.Frames;
using Rhodium.Simulation.Identity;
using Rhodium.Simulation.Modules;
using Rhodium.Simulation.Projection;

namespace Rhodium.Simulation;

/// <summary>
/// New simulation orchestrator. Exchanges own execution truth; RhodiumRuntime owns strategy-facing state.
/// </summary>
public sealed class SimulationSession : IDisposable
{
    private const decimal PriceScale = 1_000_000m;
    private const decimal QuantityScale = 1_000_000m;

    private readonly RhodiumRuntime _runtime;
    private readonly StrategyTree _tree = new();
    private readonly List<OrderIntent> _orderIntents = [];
    private readonly List<ExecutionEvent> _executionEvents = [];
    private readonly List<AccountStatementSnapshot> _accountStatements = [];
    private readonly List<FinanceEvent> _simulatorEvents = [];
    private readonly List<FinanceEvent> _pendingModuleEvents = [];
    private readonly List<SimulationModuleCommand> _pendingModuleCommands = [];
    private readonly Queue<Scheduled> _clockScheduledEvents = new();
    private readonly Dictionary<OrderId, SimulationOrderCommand> _ordersById = [];
    private readonly ExecutionEvent[] _executionBuffer = new ExecutionEvent[64];
    private readonly FinanceEvent[] _simulatorEventBuffer = new FinanceEvent[64];
    private readonly SimulationMarketProjector _marketProjector = new();
    private readonly SimulationPortfolioProjector _portfolioProjector = new();
    private readonly SimulationStructFrameProjector _structFrameProjector = new();
    private readonly HashSet<(int ModuleIndex, long TimestampNs)> _processedModuleTurns = [];
    private readonly SimulationIdentityGenerator _identity = new();
    private IReadOnlyList<VariantDescriptor> _variantDescriptors = [];
    private IReadOnlyList<ISimulationModule> _modules = [];
    private int[] _modulePreProcessCalls = [];
    private int[] _moduleProcessCalls = [];
    private int[] _moduleEmittedEvents = [];
    private int[] _moduleSubmittedCommands = [];
    private int[] _moduleEmittedFrames = [];
    private IReadOnlyList<SimulationDataProvenance> _dataProvenance = [];
    private StrategyEventProcessor? _processor;
    private Money _initialCash = Money.USD(100_000m);
    private Instant? _replayStart;
    private Instant? _replayEnd;
    private int _replayEventCount;
    private int _totalQuiescenceIterations;
    private int _maxObservedQuiescenceIterations;
    private SimulationFrameMode _frameMode;

    /// <summary>Create a simulation session with an optional runtime, deterministic clock, and default exchange config.</summary>
    public SimulationSession(
        RhodiumRuntime? runtime = null,
        IClock? clock = null,
        SimulationConfig? defaultConfig = null,
        MatchingFidelity defaultMatchingFidelity = MatchingFidelity.QueueAccurate)
    {
        _runtime = runtime ?? new RhodiumRuntime();
        Clock = clock ?? new ManualClock();
        Frames = new SimulationFrameBus();
        var config = defaultConfig ?? SimulationConfig.Instant();
        Exchanges = new SimulatedExchangeRegistry(config, _initialCash, defaultMatchingFidelity, identity: _identity);
    }

    /// <summary>Tensor, kernel, and world-state owner used by strategies during the run.</summary>
    public RhodiumRuntime Runtime => _runtime;

    /// <summary>Registered strategy hierarchy dispatched by this session.</summary>
    public StrategyTree Strategies => _tree;

    /// <summary>Venue exchange registry that owns execution truth.</summary>
    public SimulatedExchangeRegistry Exchanges { get; private set; }

    /// <summary>Local struct-frame lanes emitted by the session for low-allocation consumers.</summary>
    public SimulationFrameBus Frames { get; }

    /// <summary>Deterministic identity generator for client, venue, execution, and position IDs.</summary>
    public SimulationIdentityGenerator Identity => _identity;

    /// <summary>Authoritative simulation clock advanced by replay time.</summary>
    public IClock Clock { get; }

    /// <summary>Register a strategy in the session hierarchy.</summary>
    public StrategyId RegisterStrategy<TStrategy>(
        int depth = 0,
        IReadOnlyList<StrategyId>? children = null)
        where TStrategy : Strategy, new()
        => _tree.Register(new TStrategy(), depth, children);

    /// <summary>Set the variant descriptors used when building per-variant run results.</summary>
    public void SetVariantDescriptors(IReadOnlyList<VariantDescriptor> variants)
        => _variantDescriptors = variants;

    /// <summary>Run the session from a streaming finance-event replay source.</summary>
    public async Task<SimulationResult> RunAsync(
        IAsyncEnumerable<FinanceEvent> replay,
        SimulationRunOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(replay);
        options ??= new SimulationRunOptions();
        var readOptions = options.ReadOptions;
        return await RunStreamingAsync(replay, options, [new SimulationDataProvenance(
            "async-replay",
            Priority: 0,
            SourceOrdinal: 0,
            SourceKind: "async-replay",
            readOptions.From,
            readOptions.To,
            readOptions.EventFlowId,
            readOptions.Limit)], ct).ConfigureAwait(false);
    }

    /// <summary>Run the session from a simulation data iterator.</summary>
    public async Task<SimulationResult> RunAsync(
        SimulationDataIterator data,
        SimulationRunOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        options ??= new SimulationRunOptions();
        var readOptions = options.ReadOptions;
        return await RunStreamingAsync(
            data.ReadAsync(readOptions, ct),
            options,
            data.GetProvenance(readOptions),
            ct).ConfigureAwait(false);
    }

    /// <summary>Run the session from materialized shared history.</summary>
    public SimulationResult Run(SharedHistory history, SimulationRunOptions? options = null)
    {
        var readOptions = options?.ReadOptions ?? ReplayReadOptions.All;
        return Run(history, options, [new SimulationDataProvenance(
            "shared-history",
            Priority: 0,
            SourceOrdinal: 0,
            SourceKind: "shared-history",
            readOptions.From,
            readOptions.To,
            readOptions.EventFlowId,
            readOptions.Limit)]);
    }

    private SimulationResult Run(
        SharedHistory history,
        SimulationRunOptions? options,
        IReadOnlyList<SimulationDataProvenance> dataProvenance)
    {
        options ??= new SimulationRunOptions();
        var initialRunTime = TryGetFirstMatchingEvent(history, options.ReadOptions, out var firstEvent)
            ? GetEventTime(firstEvent)
            : GetCurrentInstant();
        InitializeRun(options, dataProvenance, initialRunTime);

        Instant? activeTimestamp = null;
        var emitted = 0;
        for (var i = 0; i < history.Count; i++)
        {
            if (options.ReadOptions.Limit is { } limit && emitted >= limit)
                break;

            var evt = history[i];
            if (!MatchesReadOptions(evt, options.ReadOptions))
                continue;

            ProcessReplayEvent(evt, options, ref activeTimestamp);
            emitted++;
        }

        return CompleteRun(options, activeTimestamp);
    }

    private async Task<SimulationResult> RunStreamingAsync(
        IAsyncEnumerable<FinanceEvent> replay,
        SimulationRunOptions options,
        IReadOnlyList<SimulationDataProvenance> dataProvenance,
        CancellationToken ct)
    {
        FinanceEvent? firstEvent = null;
        await using var enumerator = replay.GetAsyncEnumerator(ct);
        while (options.ReadOptions.Limit is not <= 0
            && await enumerator.MoveNextAsync().ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            if (!MatchesReadOptions(enumerator.Current, options.ReadOptions))
                continue;

            firstEvent = enumerator.Current;
            break;
        }

        var initialRunTime = firstEvent is null
            ? GetCurrentInstant()
            : GetEventTime(firstEvent);
        InitializeRun(options, dataProvenance, initialRunTime);

        Instant? activeTimestamp = null;
        var emitted = 0;
        if (firstEvent is not null)
        {
            ProcessReplayEvent(firstEvent, options, ref activeTimestamp);
            emitted++;
        }

        while (options.ReadOptions.Limit is not { } limit || emitted < limit)
        {
            if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                break;

            ct.ThrowIfCancellationRequested();
            var evt = enumerator.Current;
            if (!MatchesReadOptions(evt, options.ReadOptions))
                continue;

            ProcessReplayEvent(evt, options, ref activeTimestamp);
            emitted++;
        }

        return CompleteRun(options, activeTimestamp);
    }

    private void InitializeRun(
        SimulationRunOptions options,
        IReadOnlyList<SimulationDataProvenance> dataProvenance,
        Instant initialRunTime)
    {
        _orderIntents.Clear();
        _executionEvents.Clear();
        _accountStatements.Clear();
        _simulatorEvents.Clear();
        _pendingModuleEvents.Clear();
        _pendingModuleCommands.Clear();
        _clockScheduledEvents.Clear();
        _ordersById.Clear();
        _processedModuleTurns.Clear();
        _identity.Reset();
        _replayStart = null;
        _replayEnd = null;
        _replayEventCount = 0;
        _totalQuiescenceIterations = 0;
        _maxObservedQuiescenceIterations = 0;
        _initialCash = options.InitialCash;
        _dataProvenance = dataProvenance;
        _frameMode = options.FrameMode;
        InitializeModules(options);
        Exchanges = new SimulatedExchangeRegistry(
            options.Config,
            _initialCash,
            options.MatchingFidelity,
            options.VenueConfigs,
            _identity,
            processZeroLatencyCommandsImmediately: false);
        SetClock(initialRunTime);

        _processor?.Dispose();
        _processor = new StrategyEventProcessor(
            _runtime,
            _tree,
            SubmitOrderIntent)
        {
            UseParallelDispatch = options.MaxDegreeOfParallelism > 1,
            MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
            ParallelThreshold = options.MaxDegreeOfParallelism > 1 ? 1 : int.MaxValue
        };
        _processor.Initialize();
        ApplyAccountSeeds(options.AccountSeeds, initialRunTime);
        BindStrategySchedules();
    }

    private void ApplyAccountSeeds(IReadOnlyList<AccountSeed> seeds, Instant seedTime)
    {
        if (seeds.Count == 0)
            return;

        for (var i = 0; i < seeds.Count; i++)
        {
            var seed = seeds[i];
            ValidateAccountSeed(seed);
            for (var targetIndex = 0; targetIndex < _tree.NodeCount; targetIndex++)
            {
                var (_, node) = _tree.GetNode(targetIndex);
                if (!IsAccountSeedTarget(seed, node.Id, node.Depth))
                    continue;

                ApplyAccountSeedToTarget(seed, node.Id, seedTime);
            }
        }
    }

    private void ApplyAccountSeedToTarget(AccountSeed seed, StrategyId strategyId, Instant seedTime)
    {
        for (var i = 0; i < seed.Cash.Count; i++)
        {
            var cash = seed.Cash[i];
            if (cash.Amount <= 0m)
                throw new InvalidOperationException("Account seed cash entries must be positive.");

            ApplyBootstrapTransfer(new AccountTransferCompleted(
                AccountTransferId.New(),
                strategyId,
                seed.VariantId,
                AccountTransferType.CashDeposit,
                cash,
                null,
                Qty.Zero,
                seedTime,
                BuildSeedReference(seed.ExternalReference, i, "cash"),
                Venue: seed.Venue)
            {
                Time = seedTime
            });
        }

        for (var i = 0; i < seed.Positions.Count; i++)
        {
            var position = seed.Positions[i];
            if (position.Quantity.Value <= 0m)
                throw new InvalidOperationException("Account seed position quantities must be positive.");

            RegisterRuntimeContract(position.Instrument, seed.VariantId);
            ApplyBootstrapTransfer(new AccountTransferCompleted(
                AccountTransferId.New(),
                strategyId,
                seed.VariantId,
                AccountTransferType.AssetDeposit,
                null,
                position.Instrument,
                position.Quantity,
                seedTime,
                position.ExternalReference ?? BuildSeedReference(seed.ExternalReference, i, "position"),
                Venue: position.Instrument.Venue,
                CarryingPrice: position.CarryingPrice)
            {
                Time = seedTime
            });
        }
    }

    private void ApplyBootstrapTransfer(AccountTransferCompleted transfer)
    {
        if (!Exchanges.TryApplyAccountTransfer(transfer, transfer.CompletedAt, _accountStatements, out var statementCount))
        {
            throw new InvalidOperationException(
                $"Account seed transfer could not be applied for strategy {transfer.StrategyId}: cash seeds must specify a venue and position seeds must belong to a registered venue.");
        }

        _simulatorEvents.Add(transfer);
        if (statementCount > 0)
            _portfolioProjector.Apply(transfer, _runtime);
    }

    private static bool IsAccountSeedTarget(AccountSeed seed, StrategyId strategyId, int depth)
    {
        if (seed.StrategyId.HasValue)
            return seed.StrategyId.Value == strategyId;

        return depth == 0;
    }

    private static void ValidateAccountSeed(AccountSeed seed)
    {
        if (seed.Cash is null)
            throw new InvalidOperationException("Account seed cash collection cannot be null.");
        if (seed.Positions is null)
            throw new InvalidOperationException("Account seed position collection cannot be null.");
        if (seed.Cash.Count == 0 && seed.Positions.Count == 0)
            throw new InvalidOperationException("Account seed must contain cash, positions, or both.");
    }

    private static string? BuildSeedReference(string? seedReference, int index, string kind)
        => seedReference is null
            ? null
            : $"{seedReference}:{kind}:{index}";

    private void ProcessReplayEvent(
        FinanceEvent evt,
        SimulationRunOptions options,
        ref Instant? activeTimestamp)
    {
        var now = GetEventTime(evt);
        if (activeTimestamp.HasValue && now != activeTimestamp.Value)
            DrainReplayTurn(activeTimestamp.Value, options.MaxSameTimestampIterations, processModules: true);

        _replayStart ??= now;
        _replayEnd = now;
        _replayEventCount++;
        SetClock(now);
        ProcessClockScheduledEvents();
        DrainReplayTurn(now, options.MaxSameTimestampIterations, processModules: false);
        ProcessEventThroughSession(evt, preProcessModules: true);
        DrainReplayTurn(now, options.MaxSameTimestampIterations, processModules: false);
        activeTimestamp = now;
    }

    private SimulationResult CompleteRun(SimulationRunOptions options, Instant? activeTimestamp)
    {
        DrainReplayTurn(activeTimestamp ?? GetCurrentInstant(), options.MaxSameTimestampIterations, processModules: true);
        var finalTime = activeTimestamp ?? GetCurrentInstant();
        Exchanges.CompleteReplay(finalTime);
        DrainReplayTurn(finalTime, options.MaxSameTimestampIterations, processModules: false);
        return BuildResult();
    }

    private void SubmitOrderIntent(in OrderIntent intent, in MarketKernel market)
    {
        _orderIntents.Add(intent);
        var (instrument, variantId) = _runtime.BatchMap.GetContext(intent.AssetId.VirtualIndex);
        RegisterRuntimeContract(instrument, variantId);
        RegisterPackageLegContracts(instrument, variantId);
        if (intent.Kind == OrderIntentKind.Cancel)
        {
            var cancel = new SimulationCancelCommand(
                intent.StrategyId,
                variantId,
                intent.AssetId,
                instrument,
                instrument.Venue,
                intent.OrderId,
                intent.Reason);
            Exchanges.Cancel(in cancel, GetCurrentInstant());
            return;
        }

        if (intent.Kind == OrderIntentKind.Modify)
        {
            var modify = new SimulationModifyCommand(
                intent.StrategyId,
                variantId,
                intent.AssetId,
                instrument,
                instrument.Venue,
                intent.OrderId,
                intent.NewQuantity,
                intent.NewLimitPrice);
            Exchanges.Modify(in modify, GetCurrentInstant());
            return;
        }

        var command = new SimulationOrderCommand(
            intent.StrategyId,
            variantId,
            intent.AssetId,
            instrument,
            instrument.Venue,
            _identity.NextClientOrderId(),
            intent.Side,
            intent.Quantity,
            intent.Execution);

        _ordersById[command.ClientOrderId] = command;
        Exchanges.Submit(in command, GetCurrentInstant());
    }

    private static bool TryGetFirstMatchingEvent(SharedHistory history, ReplayReadOptions options, out FinanceEvent evt)
    {
        if (options.Limit is <= 0)
        {
            evt = null!;
            return false;
        }

        for (var i = 0; i < history.Count; i++)
        {
            evt = history[i];
            if (!MatchesReadOptions(evt, options))
                continue;

            return true;
        }

        evt = null!;
        return false;
    }

    private static bool MatchesReadOptions(FinanceEvent evt, ReplayReadOptions options)
    {
        if (options.EventFlowId is not null && evt.EventFlowId != options.EventFlowId)
            return false;

        var timestamp = GetEventTime(evt).ToDateTimeOffset();
        if (options.From is { } from && timestamp < from)
            return false;

        if (options.To is { } to && timestamp >= to)
            return false;

        return true;
    }

    private void DrainReplayTurn(Instant now, int maxIterations, bool processModules)
    {
        if (_processor is null)
            return;

        for (var iteration = 1; iteration <= maxIterations; iteration++)
        {
            var madeProgress = false;
            Exchanges.DrainDueWork(now);

            while (true)
            {
                var count = Exchanges.DrainExecutionEvents(_executionBuffer);
                if (count == 0)
                    break;

                madeProgress = true;
                for (var i = 0; i < count; i++)
                    ProcessExecutionEvent(_executionBuffer[i]);
            }

            while (true)
            {
                var count = Exchanges.DrainSimulationEvents(_simulatorEventBuffer);
                if (count == 0)
                    break;

                madeProgress = true;
                for (var i = 0; i < count; i++)
                    ProcessSimulatorEvent(_simulatorEventBuffer[i]);
            }

            if (processModules)
                madeProgress |= ProcessModules(now);
            _totalQuiescenceIterations++;
            _maxObservedQuiescenceIterations = Math.Max(_maxObservedQuiescenceIterations, iteration);
            if (!madeProgress && !Exchanges.HasDueWork(now))
                return;
        }

        throw new InvalidOperationException($"Replay turn exceeded max same-timestamp iterations ({maxIterations}).");
    }

    private void InitializeModules(SimulationRunOptions options)
    {
        if (options.SessionModules.Count == 0
            && options.VenueModules.Count == 0
            && options.InstrumentModules.Count == 0)
        {
            _modules = [];
        }
        else
        {
            var modules = new List<ISimulationModule>(
                options.SessionModules.Count
                + options.VenueModules.Count
                + options.InstrumentModules.Count);
            modules.AddRange(options.SessionModules);
            modules.AddRange(options.VenueModules);
            modules.AddRange(options.InstrumentModules);
            _modules = modules;
        }

        _modulePreProcessCalls = new int[_modules.Count];
        _moduleProcessCalls = new int[_modules.Count];
        _moduleEmittedEvents = new int[_modules.Count];
        _moduleSubmittedCommands = new int[_modules.Count];
        _moduleEmittedFrames = new int[_modules.Count];

        foreach (var module in _modules)
            module.Reset();
    }

    private void ProcessEventThroughSession(FinanceEvent evt, bool preProcessModules)
    {
        if (_processor is null)
            return;

        if (preProcessModules)
            PreProcessModules(evt);

        if (evt is ExecutionEvent execution)
        {
            ProcessExecutionEvent(execution);
            return;
        }

        if (evt is FinancingChargeApplied financing
            && Exchanges.TryApplyFinancing(financing, GetCurrentInstant(), out var statement))
        {
            _accountStatements.Add(statement);
        }

        if (evt is AccountTransferCompleted transfer)
        {
            if (transfer.Instrument is { } transferInstrument)
                RegisterRuntimeContract(transferInstrument, transfer.VariantId);

            if (!Exchanges.TryApplyAccountTransfer(transfer, GetCurrentInstant(), _accountStatements, out var transferStatementCount))
            {
                ProcessSimulatorEvent(new AccountTransferFailed(
                    transfer.TransferId,
                    transfer.StrategyId,
                    transfer.VariantId,
                    transfer.TransferType,
                    transfer.CashAmount,
                    transfer.Instrument,
                    transfer.Quantity,
                    GetCurrentInstant(),
                    "Account transfer could not be routed to a simulated venue. Cash transfers must specify a venue when multiple or no venues are active.",
                    transfer.ExternalReference,
                    transfer.DestinationStrategyId,
                    transfer.DestinationVariantId,
                    transfer.Venue,
                    transfer.CarryingPrice)
                {
                    Time = GetCurrentInstant()
                });
                return;
            }

            _simulatorEvents.Add(transfer);
            var transferTransition = transferStatementCount > 0
                ? _portfolioProjector.Apply(transfer, _runtime)
                : StateTransitionResult.None;
            _structFrameProjector.Apply(transfer, _runtime, Frames, _frameMode);
            _processor.ProcessProjectedEvent(evt, in transferTransition);
            return;
        }

        if (IsAccountTransferLifecycleEvent(evt))
        {
            ProcessSimulatorEvent(evt);
            return;
        }

        if (evt is MarketEvent marketEvent)
            RegisterRuntimeContract(marketEvent.Instrument, variantId: 0);

        var routedByFrame = TryRouteFrameNativeMarketEvent(evt);
        if (!routedByFrame)
            Exchanges.OnMarketEvent(evt);

        var transition = _marketProjector.Apply(evt, _runtime);
        if (!routedByFrame)
            _structFrameProjector.Apply(evt, _runtime, Frames, _frameMode);
        _processor.ProcessProjectedEvent(evt, in transition);
    }

    private void ProcessExecutionEvent(ExecutionEvent evt)
    {
        if (_processor is null)
            return;

        evt = evt with { Time = GetCurrentInstant() };
        _executionEvents.Add(evt);
        var transition = _portfolioProjector.Apply(evt, _runtime, TryGetAssetId(evt));
        _structFrameProjector.Apply(evt, _runtime, Frames, _frameMode);
        _processor.ProcessProjectedEvent(evt, in transition);
        if (evt is OrderFilled fill)
            CaptureFillAccountDiagnostics(fill);
    }

    private void CaptureFillAccountDiagnostics(OrderFilled fill)
    {
        var now = GetCurrentInstant();
        if (Exchanges.TryCreatePerformanceSnapshot(fill, now, out var performance))
            _simulatorEvents.Add(performance);

        if (Exchanges.TryCreateAccountStatement(fill, now, out var statement))
            _accountStatements.Add(statement);
    }

    private void RegisterRuntimeContract(Instrument instrument, int variantId)
    {
        if (_runtime.TryGetContract(instrument, out var contract, variantId))
        {
            Exchanges.RegisterContract(contract);
            return;
        }

        if (_runtime.TryGetContract(instrument, out contract))
        {
            Exchanges.RegisterContract(contract);
            return;
        }

        throw new InvalidOperationException(
            $"Instrument {instrument} has no runtime InstrumentContract. Register the instrument through a contract-aware setup API before routing simulation orders or account events.");
    }

    private void RegisterPackageLegContracts(Instrument instrument, int variantId)
    {
        if (!_runtime.TryGetContract(instrument, out var packageContract, variantId) ||
            packageContract.Package is null)
        {
            return;
        }

        for (var i = 0; i < packageContract.Legs.Count; i++)
        {
            var leg = packageContract.Legs[i];
            if (_runtime.TryGetContract(leg.Instrument, out var legContract, variantId))
            {
                Exchanges.RegisterContract(legContract);
            }
        }
    }

    private void ProcessSimulatorEvent(FinanceEvent evt)
    {
        _simulatorEvents.Add(evt);
        if (_processor is null)
            return;

        var transition = evt switch
        {
            CorporateActionEffectSnapshot corporateAction => _portfolioProjector.Apply(corporateAction, _runtime),
            OptionLifecycleApplied optionLifecycle => _portfolioProjector.Apply(optionLifecycle, _runtime),
            _ => _marketProjector.Apply(evt, _runtime)
        };
        _structFrameProjector.Apply(evt, _runtime, Frames, _frameMode);
        _processor.ProcessProjectedEvent(evt, in transition);
        if (evt is OptionLifecycleApplied lifecycle
            && Exchanges.TryCreateAccountStatement(lifecycle, GetCurrentInstant(), out var statement))
        {
            _accountStatements.Add(statement);
        }
    }

    private static bool IsAccountTransferLifecycleEvent(FinanceEvent evt)
        => evt is AccountTransferRequested
            or AccountTransferCanceled
            or AccountTransferFailed
            or AccountTransferStatusSnapshot;

    private void PreProcessModules(FinanceEvent evt)
    {
        if (_modules.Count == 0)
            return;

        var context = CreateModuleContext();
        for (var i = 0; i < _modules.Count; i++)
        {
            if (!ShouldPreProcessModule(_modules[i], evt))
                continue;

            _modulePreProcessCalls[i]++;
            var beforeEvents = _pendingModuleEvents.Count;
            var beforeCommands = _pendingModuleCommands.Count;
            var sinks = CreateModuleSinks(i);
            _modules[i].PreProcess(in evt, ref context, ref sinks);
            _moduleEmittedEvents[i] += _pendingModuleEvents.Count - beforeEvents;
            _moduleSubmittedCommands[i] += _pendingModuleCommands.Count - beforeCommands;
            DrainModuleCommands();
            DrainModuleEvents(GetCurrentInstant());
        }
    }

    private bool ProcessModules(Instant now)
    {
        if (_modules.Count == 0)
            return false;

        var madeProgress = false;
        var context = CreateModuleContext();
        for (var i = 0; i < _modules.Count; i++)
        {
            if (!ShouldProcessModule(_modules[i]))
                continue;

            if (!_processedModuleTurns.Add((i, now.Nanos)))
                continue;

            _moduleProcessCalls[i]++;
            var beforeEvents = _pendingModuleEvents.Count;
            var beforeCommands = _pendingModuleCommands.Count;
            var sinks = CreateModuleSinks(i);
            _modules[i].Process(now, ref context, ref sinks);
            var emittedEvents = _pendingModuleEvents.Count - beforeEvents;
            var submittedCommands = _pendingModuleCommands.Count - beforeCommands;
            _moduleEmittedEvents[i] += emittedEvents;
            _moduleSubmittedCommands[i] += submittedCommands;
            madeProgress |= submittedCommands > 0;
            DrainModuleCommands();
            madeProgress |= DrainModuleEvents(now);
        }

        return madeProgress;
    }

    private SimulationModuleContext CreateModuleContext()
        => new(_runtime, Exchanges, Clock);

    private bool ShouldPreProcessModule(ISimulationModule module, FinanceEvent evt)
        => module switch
        {
            ISessionSimulationModule => true,
            IVenueSimulationModule venueModule => TryGetEventVenue(evt, out var venue) && venue == venueModule.Venue,
            IInstrumentSimulationModule instrumentModule => evt is MarketEvent market && market.Instrument == instrumentModule.Instrument,
            _ => true
        };

    private bool ShouldProcessModule(ISimulationModule module)
        => module switch
        {
            ISessionSimulationModule => true,
            IVenueSimulationModule venueModule => Exchanges.TryGet(venueModule.Venue, out _),
            IInstrumentSimulationModule instrumentModule
                => Exchanges.TryGet(instrumentModule.Instrument.Venue, out var exchange)
                   && exchange.TryGetInstrumentEngine(instrumentModule.Instrument, out _),
            _ => true
        };

    private static bool TryGetEventVenue(FinanceEvent evt, out Venue venue)
    {
        switch (evt)
        {
            case VenueStatusChanged venueStatus:
                venue = venueStatus.Venue;
                return true;
            case MarketOpened opened:
                venue = opened.Venue;
                return true;
            case MarketClosed closed:
                venue = closed.Venue;
                return true;
            case PreMarketOpened preMarket:
                venue = preMarket.Venue;
                return true;
            case PostMarketOpened postMarket:
                venue = postMarket.Venue;
                return true;
            case CorporateActionApplied corporateAction:
                venue = corporateAction.Instrument.Venue;
                return true;
            case OptionAssignmentNoticePublished assignmentNotice:
                venue = assignmentNotice.Instrument.Venue;
                return true;
            case AccountTransferCompleted transfer when transfer.Instrument is { } instrument:
                venue = instrument.Venue;
                return true;
            case AccountTransferCompleted transfer when transfer.Venue.HasValue:
                venue = transfer.Venue.Value;
                return true;
            case MarketEvent market:
                venue = market.Instrument.Venue;
                return true;
            default:
                venue = default;
                return false;
        }
    }

    private SimulationModuleSinks CreateModuleSinks(int moduleIndex)
        => new(_pendingModuleEvents, _pendingModuleCommands, Frames, _frameMode, _moduleEmittedFrames, moduleIndex);

    private void DrainModuleCommands()
    {
        if (_pendingModuleCommands.Count == 0)
            return;

        var now = GetCurrentInstant();
        for (var i = 0; i < _pendingModuleCommands.Count; i++)
        {
            var command = _pendingModuleCommands[i];
            switch (command.Kind)
            {
                case SimulationModuleCommandKind.Submit:
                    var submit = command.Submit;
                    RegisterRuntimeContract(submit.Instrument, submit.VariantId);
                    _ordersById[submit.ClientOrderId] = submit;
                    Exchanges.Submit(in submit, now);
                    break;
                case SimulationModuleCommandKind.Cancel:
                    var cancel = command.Cancel;
                    RegisterRuntimeContract(cancel.Instrument, cancel.VariantId);
                    Exchanges.Cancel(in cancel, now);
                    break;
                case SimulationModuleCommandKind.Modify:
                    var modify = command.Modify;
                    RegisterRuntimeContract(modify.Instrument, modify.VariantId);
                    Exchanges.Modify(in modify, now);
                    break;
            }
        }

        _pendingModuleCommands.Clear();
    }

    private bool DrainModuleEvents(Instant now)
    {
        if (_pendingModuleEvents.Count == 0)
            return false;

        for (var i = 0; i < _pendingModuleEvents.Count; i++)
        {
            var evt = _pendingModuleEvents[i] with { Time = now };
            if (IsModuleObservableEvent(evt))
                _simulatorEvents.Add(evt);

            ProcessEventThroughSession(evt, preProcessModules: false);
        }

        _pendingModuleEvents.Clear();
        return true;
    }

    private static bool IsModuleObservableEvent(FinanceEvent evt)
        => evt is MarketEvent
            or VenueStatusChanged
            or MarketOpened
            or MarketClosed
            or PreMarketOpened
            or PostMarketOpened;

    private bool TryRouteFrameNativeMarketEvent(FinanceEvent evt)
    {
        switch (evt)
        {
            case BookOrderAdded added:
                if (!TryGetInstrumentIndex(added.Instrument, out var addedIndex))
                    return false;

                var addedFrame = new BookOrderAddedFrame(
                    addedIndex,
                    added.Order.OrderId.Value,
                    added.Order.Side,
                    ScalePrice(added.Order.Price),
                    ScaleQty(added.Order.Size),
                    added.VenueSequence,
                    added.Time.Nanos);
                if (_frameMode is SimulationFrameMode.MarketData or SimulationFrameMode.All)
                    Frames.Emit(in addedFrame);
                Exchanges.OnBookOrderAdded(added.Instrument, in addedFrame);
                return true;

            case BookOrderModified modified:
                if (!TryGetInstrumentIndex(modified.Instrument, out var modifiedIndex))
                    return false;

                var modifiedFrame = new BookOrderModifiedFrame(
                    modifiedIndex,
                    modified.Order.OrderId.Value,
                    modified.Order.Side,
                    ScalePrice(modified.Order.Price),
                    ScaleQty(modified.Order.Size),
                    modified.VenueSequence,
                    modified.Time.Nanos);
                if (_frameMode is SimulationFrameMode.MarketData or SimulationFrameMode.All)
                    Frames.Emit(in modifiedFrame);
                Exchanges.OnBookOrderModified(modified.Instrument, in modifiedFrame);
                return true;

            case BookOrderDeleted deleted:
                if (!TryGetInstrumentIndex(deleted.Instrument, out var deletedIndex))
                    return false;

                var deletedFrame = new BookOrderDeletedFrame(
                    deletedIndex,
                    deleted.OrderId.Value,
                    deleted.VenueSequence,
                    deleted.Time.Nanos);
                if (_frameMode is SimulationFrameMode.MarketData or SimulationFrameMode.All)
                    Frames.Emit(in deletedFrame);
                Exchanges.OnBookOrderDeleted(deleted.Instrument, in deletedFrame);
                return true;

            case BookOrderExecuted executed:
                if (!TryGetInstrumentIndex(executed.Instrument, out var executedIndex))
                    return false;

                var executedFrame = new BookOrderExecutedFrame(
                    executedIndex,
                    executed.OrderId.Value,
                    ScaleQty(executed.ExecutedSize),
                    executed.VenueSequence,
                    executed.Time.Nanos);
                if (_frameMode is SimulationFrameMode.MarketData or SimulationFrameMode.All)
                    Frames.Emit(in executedFrame);
                Exchanges.OnBookOrderExecuted(executed.Instrument, in executedFrame);
                return true;

            default:
                return false;
        }
    }

    private void BindStrategySchedules()
    {
        Clock.CancelAllTimers();
        for (var i = 0; i < _tree.NodeCount; i++)
        {
            var (strategy, _) = _tree.GetNode(i);
            foreach (var schedule in strategy.Schedules)
                BindStrategySchedule(strategy.Id, schedule);
        }
    }

    private void BindStrategySchedule(StrategyId strategyId, StrategySchedule schedule)
    {
        if (schedule.IsRecurring)
        {
            Clock.SetTimer(
                schedule.Name,
                schedule.Interval.ToTimeSpan(),
                timeEvent => EnqueueScheduledEvent(strategyId, timeEvent),
                schedule.FireAt?.ToDateTimeOffset(),
                schedule.StopAt?.ToDateTimeOffset());
            return;
        }

        var fireAt = schedule.FireAt
            ?? throw new InvalidOperationException($"One-shot strategy schedule {schedule.Name} must specify a fire time.");
        Clock.SetAlert(
            schedule.Name,
            fireAt.ToDateTimeOffset(),
            timeEvent => EnqueueScheduledEvent(strategyId, timeEvent));
    }

    private void EnqueueScheduledEvent(StrategyId strategyId, TimeEvent timeEvent)
        => _clockScheduledEvents.Enqueue(new Scheduled(timeEvent.TimerName, strategyId)
        {
            Time = Instant.FromDateTimeOffset(timeEvent.TriggerTime)
        });

    private void ProcessClockScheduledEvents()
    {
        while (_clockScheduledEvents.TryDequeue(out var scheduled))
            ProcessEventThroughSession(scheduled, preProcessModules: true);
    }

    private SimulationResult BuildResult()
    {
        var runs = new StrategyRunResult[_tree.NodeCount];
        var universeSize = _runtime.BatchMap.TotalSize;
        var strategyFills = new List<OrderFilled>();

        for (var runIndex = 0; runIndex < _tree.NodeCount; runIndex++)
        {
            var (strategy, _) = _tree.GetNode(runIndex);
            var variantIndex = runIndex;
            var parameters = ParameterSet.Empty;
            if (TryGetVariantDescriptor(strategy.Id, out var variant))
            {
                variantIndex = variant.VariantIndex;
                parameters = variant.Parameters;
            }

            strategyFills.Clear();
            for (var i = 0; i < _executionEvents.Count; i++)
            {
                if (_executionEvents[i] is OrderFilled fill && fill.StrategyId == strategy.Id)
                    strategyFills.Add(fill);
            }

            var roundTrips = RoundTripBuilder.FromFills(strategyFills).ToArray();

            runs[runIndex] = new StrategyRunResult(
                strategy.Id,
                variantIndex,
                parameters,
                TearSheet.Calculate(roundTrips, _initialCash),
                FinalSnapshot: _runtime.WorldState.BuildSnapshot(strategy.Id, universeSize, GetCurrentInstant()));
        }

        var tearSheets = new TearSheet[runs.Length];
        for (var i = 0; i < runs.Length; i++)
            tearSheets[i] = runs[i].TearSheet;

        var batch = BatchTearSheetBuilder.FromTearSheets(tearSheets);
        return new SimulationResult(
            runs,
            batch,
            _orderIntents.ToArray(),
            _executionEvents.ToArray(),
            _accountStatements.ToArray(),
            _simulatorEvents.ToArray(),
            BuildDiagnostics());
    }

    private bool TryGetVariantDescriptor(StrategyId strategyId, out VariantDescriptor descriptor)
    {
        for (var i = 0; i < _variantDescriptors.Count; i++)
        {
            if (_variantDescriptors[i].StrategyId == strategyId)
            {
                descriptor = _variantDescriptors[i];
                return true;
            }
        }

        descriptor = default;
        return false;
    }

    private SimulationDiagnostics BuildDiagnostics()
    {
        var instrumentCount = 0;
        var rejectionCount = 0;
        foreach (var exchange in Exchanges.VenueValues)
        {
            instrumentCount += exchange.InstrumentEngineCount;
            foreach (var engine in exchange.EngineValues)
                rejectionCount += engine.RejectionDiagnosticCount;
        }

        var venueDiagnostics = new VenueSimulationDiagnostics[Exchanges.VenueCount];
        var instrumentDiagnostics = new InstrumentSimulationDiagnostics[instrumentCount];
        var rejections = new SimulationRejectionDiagnostic[rejectionCount];
        var venueIndex = 0;
        var instrumentIndex = 0;
        var rejectionIndex = 0;

        foreach (var exchange in Exchanges.VenueValues)
        {
            var engineCount = 0;
            var accepted = 0;
            var rejected = 0;
            var filled = 0;
            var cancelled = 0;
            var expired = 0;

            foreach (var engine in exchange.EngineValues)
            {
                engineCount++;
                accepted += engine.AcceptedOrders;
                rejected += engine.RejectedOrders;
                filled += engine.FilledOrders;
                cancelled += engine.CancelledOrders;
                expired += engine.ExpiredOrders;

                instrumentDiagnostics[instrumentIndex++] = new InstrumentSimulationDiagnostics(
                    engine.Instrument,
                    engine.Status,
                    engine.MatchingFidelity,
                    engine.OrderPolicy,
                    engine.SimulationPolicy,
                    engine.TryGetMarkPrice(out var mark) ? mark : null,
                    engine.CloseMark,
                    engine.OpenOrders,
                    engine.AcceptedOrders,
                    engine.RejectedOrders,
                    engine.FilledOrders,
                    engine.CancelledOrders,
                    engine.ExpiredOrders);

                rejectionIndex += engine.CopyRejections(rejections.AsSpan(rejectionIndex));
            }

            venueDiagnostics[venueIndex++] = new VenueSimulationDiagnostics(
                exchange.Venue,
                exchange.Status,
                exchange.Account.AccountType,
                exchange.Account.Cash.Currency,
                engineCount,
                exchange.SubmittedCommands,
                accepted,
                rejected,
                filled,
                cancelled,
                expired,
                exchange.Account.Cash,
                exchange.Account.AvailableCash,
                exchange.Account.ReservedCash,
                exchange.Account.PendingSettlement,
                exchange.Account.PendingSettlementCount,
                exchange.Account.PendingAssetDeliveryQuantity,
                exchange.Account.PendingAssetDeliveryCount,
                exchange.OrderPolicy,
                exchange.SimulationPolicy);
        }

        return new SimulationDiagnostics(
            venueDiagnostics,
            instrumentDiagnostics,
            new QuiescenceDiagnostics(_maxObservedQuiescenceIterations, _totalQuiescenceIterations),
            BuildLatencyDiagnostics(),
            new RunTimingDiagnostics(_replayStart, _replayEnd, GetCurrentInstant(), _replayEventCount),
            BuildModuleDiagnostics(),
            Frames.GetStats(),
            _dataProvenance,
            rejections);
    }

    private SimulationModuleDiagnostics[] BuildModuleDiagnostics()
    {
        var diagnostics = new SimulationModuleDiagnostics[_modules.Count];
        for (var i = 0; i < _modules.Count; i++)
        {
            var counters = new List<SimulationModuleCounter>();
            var metrics = new List<SimulationModuleMetric>();
            var messages = new List<SimulationModuleMessage>();
            var builder = new SimulationDiagnosticsBuilder(counters, metrics, messages);
            _modules[i].AppendDiagnostics(ref builder);

            diagnostics[i] = new SimulationModuleDiagnostics(
                _modules[i].GetType().Name,
                _modulePreProcessCalls[i],
                _moduleProcessCalls[i],
                _moduleEmittedEvents[i],
                _moduleSubmittedCommands[i],
                _moduleEmittedFrames[i],
                counters,
                metrics,
                messages);
        }

        return diagnostics;
    }

    private LatencyDiagnostics BuildLatencyDiagnostics()
    {
        var commandCount = 0;
        long min = 0;
        long max = 0;
        long total = 0;
        var hasSamples = false;

        foreach (var exchange in Exchanges.VenueValues)
        {
            if (exchange.LatencySampleCount == 0)
                continue;

            commandCount += exchange.LatencySampleCount;
            total += exchange.TotalEntryLatencyNanos;
            var exchangeMin = exchange.MinEntryLatency.Nanos;
            var exchangeMax = exchange.MaxEntryLatency.Nanos;
            if (!hasSamples)
            {
                min = exchangeMin;
                max = exchangeMax;
                hasSamples = true;
            }
            else
            {
                if (exchangeMin < min)
                    min = exchangeMin;
                if (exchangeMax > max)
                    max = exchangeMax;
            }
        }

        if (commandCount == 0)
            return new LatencyDiagnostics(0, Duration.Zero, Duration.Zero, Duration.Zero);

        return new LatencyDiagnostics(
            commandCount,
            Duration.FromNanos(min),
            Duration.FromNanos(max),
            Duration.FromNanos(total / commandCount));
    }

    private void SetClock(Instant now)
    {
        _runtime.SetTime(now);
        if (Clock is ManualClock manual)
            manual.Set(now.ToDateTimeOffset());
    }

    private Instant GetCurrentInstant()
        => Instant.FromDateTimeOffset(Clock.UtcNow);

    private AssetId? TryGetAssetId(ExecutionEvent evt)
        => evt switch
        {
            OrderAccepted accepted when _ordersById.TryGetValue(accepted.OrderId, out var command) => command.AssetId,
            OrderModified modified when _ordersById.TryGetValue(modified.OrderId, out var command) => command.AssetId,
            OrderRejected rejected when _ordersById.TryGetValue(rejected.OrderId, out var command) => command.AssetId,
            OrderCancelled cancelled when _ordersById.TryGetValue(cancelled.OrderId, out var command) => command.AssetId,
            OrderExpired expired when _ordersById.TryGetValue(expired.OrderId, out var command) => command.AssetId,
            OrderFilled filled when _ordersById.TryGetValue(filled.OrderId, out var command) => command.AssetId,
            OrderFilled filled => TryGetAssetId(filled.Instrument, filled.VariantId),
            _ => null
        };

    private AssetId? TryGetAssetId(Instrument instrument, int variantId)
    {
        try
        {
            var range = _runtime.BatchMap.GetInstrumentRange(instrument);
            if (variantId < 0 || variantId >= range.Length)
                return null;

            return new AssetId(range.Start + variantId);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    private bool TryGetInstrumentIndex(Instrument instrument, out int instrumentIndex)
    {
        try
        {
            var range = _runtime.BatchMap.GetInstrumentRange(instrument);
            instrumentIndex = range.Start;
            return true;
        }
        catch (KeyNotFoundException)
        {
            instrumentIndex = 0;
            return false;
        }
    }

    private static long ScalePrice(Price price)
        => DecimalToInt64(price.Value * PriceScale);

    private static long ScaleQty(Qty qty)
        => DecimalToInt64(qty.Value * QuantityScale);

    private static long DecimalToInt64(decimal value)
        => decimal.ToInt64(decimal.Round(value, 0, MidpointRounding.AwayFromZero));

    private static Instant GetEventTime(FinanceEvent evt)
        => evt switch
        {
            QuoteReceived quote => quote.Quote.Time.ExchangeTime,
            TradeOccurred trade => trade.Trade.Time.ExchangeTime,
            BarClosed bar => bar.Bar.Time,
            BookSnapshotReceived book => book.Book.Time,
            BookDepthSnapshotReceived snapshot => snapshot.Time,
            BookDepth10Received snapshot => snapshot.Time,
            SettlementReferencePricePublished settlement => settlement.EffectiveAt,
            OptionAssignmentNoticePublished assignment => assignment.EffectiveAt,
            _ => evt.Time
        };

    /// <inheritdoc />
    public void Dispose()
    {
        Clock.CancelAllTimers();
        _processor?.Dispose();
        Frames.Dispose();
        _runtime.Dispose();
    }
}
