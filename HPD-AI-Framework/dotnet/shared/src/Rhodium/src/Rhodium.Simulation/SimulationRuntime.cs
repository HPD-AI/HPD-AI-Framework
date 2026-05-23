using Rhodium.Events;
using Rhodium.Analytics;
using Rhodium.Kernel;
using Rhodium.Platform;
using Rhodium.Platform.Patterns;
using Rhodium.Primitives;

namespace Rhodium.Simulation;

public sealed class SimulationRuntime : IDisposable
{
    private readonly RhodiumRuntime _runtime;
    private readonly StrategyTree _tree = new();
    private readonly List<OrderIntent> _orderIntents = [];
    private readonly List<ExecutionEvent> _executionEvents = [];
    private readonly ExecutionEvent[] _executionEventBuffer = new ExecutionEvent[64];
    private IReadOnlyList<VariantDescriptor> _variantDescriptors = [];
    private StrategyEventProcessor? _processor;
    private ISimulationExecutionModel? _executionModel;
    private Money _initialCash = Money.USD(100_000m);
    private bool _ownsExecutionModel;

    public SimulationRuntime(RhodiumRuntime? runtime = null)
    {
        _runtime = runtime ?? new RhodiumRuntime();
    }

    public RhodiumRuntime Runtime => _runtime;

    public StrategyTree Strategies => _tree;

    public StrategyId RegisterStrategy<TStrategy>(
        int depth = 0,
        IReadOnlyList<StrategyId>? children = null)
        where TStrategy : Strategy, new()
        => _tree.Register(new TStrategy(), depth, children);

    public void SetVariantDescriptors(IReadOnlyList<VariantDescriptor> variants)
        => _variantDescriptors = variants;

    public SimulationResult Run(SharedHistory history, SimulationRunOptions? options = null)
    {
        options ??= new SimulationRunOptions();
        _orderIntents.Clear();
        _executionEvents.Clear();
        _initialCash = options.InitialCash;
        _processor?.Dispose();
        ConfigureExecutionModel(options);

        _processor = new StrategyEventProcessor(
            _runtime,
            _tree,
            SubmitOrderIntent,
            OnProcessedMarketEvent)
        {
            UseParallelDispatch = options.MaxDegreeOfParallelism > 1,
            MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
            ParallelThreshold = options.MaxDegreeOfParallelism > 1 ? 1 : int.MaxValue
        };
        _processor.Initialize();

        foreach (var evt in history.Span)
        {
            _processor.ProcessEvent(evt);
            ProcessExecutionEvents();
        }

        return BuildResult();
    }

    private void ConfigureExecutionModel(SimulationRunOptions options)
    {
        if (_ownsExecutionModel)
            _executionModel?.Dispose();

        _executionModel = options.ExecutionModel ?? CreateExecutionModel(options.Config.Fidelity);
        _ownsExecutionModel = options.ExecutionModel is null;
        _executionModel.Initialize(new SimulationExecutionContext(_runtime, options.Config));
    }

    private static ISimulationExecutionModel CreateExecutionModel(SimulationFidelity fidelity)
        => fidelity == SimulationFidelity.Vector
            ? new VectorExecutionModel()
            : new QueueExecutionModel();

    private void OnProcessedMarketEvent(FinanceEvent evt, in MarketKernel market)
    {
        if (evt is MarketEvent)
            _executionModel?.OnMarketEvent(evt, in market);
    }

    private void SubmitOrderIntent(in OrderIntent intent, in MarketKernel market)
    {
        _orderIntents.Add(intent);
        _executionModel?.Submit(in intent, in market);
    }

    private void ProcessExecutionEvents()
    {
        if (_executionModel is null || _processor is null)
            return;

        while (true)
        {
            var count = _executionModel.DrainExecutionEvents(_executionEventBuffer);
            if (count == 0)
                return;

            for (var i = 0; i < count; i++)
            {
                var evt = _executionEventBuffer[i];
                _executionEvents.Add(evt);
                _processor.ProcessEvent(evt);
            }
        }
    }

    private SimulationResult BuildResult()
    {
        var runs = new List<StrategyRunResult>(_tree.Nodes.Count);
        var universeSize = _runtime.BatchMap.TotalSize;
        var fills = _executionEvents.OfType<OrderFilled>().ToArray();
        var variantsByStrategyId = _variantDescriptors.Count == 0
            ? null
            : _variantDescriptors.ToDictionary(static variant => variant.StrategyId);

        foreach (var (strategy, _) in _tree.Nodes)
        {
            var variantIndex = runs.Count;
            var parameters = new ParameterSet(new Dictionary<string, object>());
            if (variantsByStrategyId is not null && variantsByStrategyId.TryGetValue(strategy.Id, out var variant))
            {
                variantIndex = variant.VariantIndex;
                parameters = variant.Parameters;
            }

            var roundTrips = RoundTripBuilder
                .FromFills(fills.Where(fill => fill.StrategyId == strategy.Id))
                .ToArray();

            runs.Add(new StrategyRunResult(
                strategy.Id,
                variantIndex,
                parameters,
                TearSheet.Calculate(roundTrips, _initialCash),
                FinalSnapshot: _runtime.WorldState.BuildSnapshot(strategy.Id, universeSize)));
        }

        var batch = BatchTearSheetBuilder.FromTearSheets(runs.Select(static run => run.TearSheet).ToArray());
        return new SimulationResult(runs, batch, _orderIntents.ToArray(), _executionEvents.ToArray());
    }

    public void Dispose()
    {
        _processor?.Dispose();
        if (_ownsExecutionModel)
            _executionModel?.Dispose();
        _runtime.Dispose();
    }
}
