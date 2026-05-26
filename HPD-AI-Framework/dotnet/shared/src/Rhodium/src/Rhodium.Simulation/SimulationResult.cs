using Rhodium.Events;
using Rhodium.Analytics;
using Rhodium.Platform;
using Rhodium.Primitives;
using Rhodium.Simulation.Diagnostics;

namespace Rhodium.Simulation;

/// <summary>
/// Completed simulation output, including strategy performance and exchange/session diagnostics.
/// </summary>
public sealed class SimulationResult
{
    /// <summary>Create a completed simulation result.</summary>
    public SimulationResult(
        IReadOnlyList<StrategyRunResult> runs,
        BatchTearSheet batch,
        IReadOnlyList<OrderIntent> orderIntents,
        IReadOnlyList<ExecutionEvent> executionEvents,
        IReadOnlyList<AccountStatementSnapshot>? accountStatements = null,
        IReadOnlyList<FinanceEvent>? simulatorEvents = null,
        SimulationDiagnostics? diagnostics = null)
    {
        Runs = runs;
        Batch = batch;
        OrderIntents = orderIntents;
        ExecutionEvents = executionEvents;
        AccountStatements = accountStatements ?? [];
        SimulatorEvents = simulatorEvents ?? [];
        Diagnostics = diagnostics ?? SimulationDiagnostics.Empty;
    }

    /// <summary>Per-strategy and per-variant results.</summary>
    public IReadOnlyList<StrategyRunResult> Runs { get; }

    /// <summary>Batch-level performance summary.</summary>
    public BatchTearSheet Batch { get; }

    /// <summary>Strategy intents captured during the run.</summary>
    public IReadOnlyList<OrderIntent> OrderIntents { get; }

    /// <summary>Execution events emitted by simulated exchanges.</summary>
    public IReadOnlyList<ExecutionEvent> ExecutionEvents { get; }

    /// <summary>Account statements emitted by venue accounts.</summary>
    public IReadOnlyList<AccountStatementSnapshot> AccountStatements { get; }

    /// <summary>Non-execution simulator events emitted during the run.</summary>
    public IReadOnlyList<FinanceEvent> SimulatorEvents { get; }

    /// <summary>Exchange, instrument, latency, quiescence, and data diagnostics.</summary>
    public SimulationDiagnostics Diagnostics { get; }

    /// <summary>Create an analyzer for vector and parameter-grid result exploration.</summary>
    public VectorScanAnalyzer Analyze() => new(this);

    /// <summary>Return the top runs by Sharpe ratio.</summary>
    public IReadOnlyList<StrategyRunResult> TopBySharpe(int count)
        => Analyze().TopBySharpe(count);

    /// <summary>Return the top runs by total return.</summary>
    public IReadOnlyList<StrategyRunResult> TopByTotalReturn(int count)
        => Analyze().TopByTotalReturn(count);

    /// <summary>Recover the parameter grid represented by this result.</summary>
    public ParameterGrid ToParameterGrid() => Runs.ToParameterGrid();
}

/// <summary>
/// Convenience conversion helpers for strategy run results.
/// </summary>
public static class StrategyRunResultExtensions
{
    /// <summary>Create a parameter grid from strategy run parameter sets.</summary>
    public static ParameterGrid ToParameterGrid(this IReadOnlyList<StrategyRunResult> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var parameters = new ParameterSet[runs.Count];
        for (var i = 0; i < runs.Count; i++)
            parameters[i] = runs[i].Parameters;

        return ParameterGrid.FromParameterSets(parameters);
    }
}
