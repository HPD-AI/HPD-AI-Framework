using HPD.Events;
using HPD.Events.Core;
using Rhodium.Events;

namespace Rhodium.Simulation.Data;

/// <summary>
/// A named finance replay source used by a simulation run.
/// </summary>
public sealed record SimulationDataSource(
    string SourceId,
    IReplaySource<FinanceEvent> Source,
    int Priority = 0,
    string SourceKind = "replay-source")
{
    /// <summary>Create a source from an in-memory enumerable of finance events.</summary>
    public static SimulationDataSource FromEvents(
        string sourceId,
        IEnumerable<FinanceEvent> events,
        int priority = 0,
        string sourceKind = "enumerable")
    {
        ArgumentNullException.ThrowIfNull(events);
        return new SimulationDataSource(
            sourceId,
            new EnumerableReplaySource<FinanceEvent>(events),
            priority,
            sourceKind);
    }

    /// <summary>Create a source from an async enumerable of finance events.</summary>
    public static SimulationDataSource FromAsyncEvents(
        string sourceId,
        IAsyncEnumerable<FinanceEvent> events,
        int priority = 0,
        string sourceKind = "async-enumerable")
    {
        ArgumentNullException.ThrowIfNull(events);
        return new SimulationDataSource(
            sourceId,
            new AsyncEnumerableReplaySource<FinanceEvent>(events),
            priority,
            sourceKind);
    }
}
