using Rhodium.Events;
using Rhodium.Analytics;
using Rhodium.Platform;
using Rhodium.Primitives;

namespace Rhodium.Simulation;

public sealed class SimulationResult
{
    public SimulationResult(
        IReadOnlyList<StrategyRunResult> runs,
        BatchTearSheet batch,
        IReadOnlyList<OrderIntent> orderIntents,
        IReadOnlyList<ExecutionEvent> executionEvents)
    {
        Runs = runs;
        Batch = batch;
        OrderIntents = orderIntents;
        ExecutionEvents = executionEvents;
    }

    public IReadOnlyList<StrategyRunResult> Runs { get; }

    public BatchTearSheet Batch { get; }

    public IReadOnlyList<OrderIntent> OrderIntents { get; }

    public IReadOnlyList<ExecutionEvent> ExecutionEvents { get; }

    public VectorScanAnalyzer Analyze() => new(this);

    public IEnumerable<StrategyRunResult> TopBySharpe(int count)
        => Analyze().TopBySharpe(count);

    public IEnumerable<StrategyRunResult> TopByTotalReturn(int count)
        => Analyze().TopByTotalReturn(count);

    public ParameterGrid ToParameterGrid() => Runs.ToParameterGrid();
}

public static class StrategyRunResultExtensions
{
    public static ParameterGrid ToParameterGrid(this IEnumerable<StrategyRunResult> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        return ParameterGrid.FromParameterSets(runs.Select(static run => run.Parameters));
    }
}
