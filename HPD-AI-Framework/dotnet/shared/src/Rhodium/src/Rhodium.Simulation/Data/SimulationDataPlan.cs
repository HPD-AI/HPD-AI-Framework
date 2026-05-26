using HPD.Events;
using Rhodium.Events;

namespace Rhodium.Simulation.Data;

/// <summary>
/// Finance replay inputs and read options for a simulation run.
/// </summary>
public sealed class SimulationDataPlan
{
    private readonly List<SimulationDataSource> _sources = [];

    private SimulationDataPlan(ReplayReadOptions readOptions)
    {
        ReadOptions = readOptions;
    }

    /// <summary>Create an empty data plan with optional base read options.</summary>
    public static SimulationDataPlan Create(ReplayReadOptions? readOptions = null)
        => new(readOptions ?? ReplayReadOptions.All);

    /// <summary>Base read options applied before run-level read options.</summary>
    public ReplayReadOptions ReadOptions { get; private set; }

    /// <summary>Number of replay sources in this plan.</summary>
    public int SourceCount => _sources.Count;

    /// <summary>Get one replay source by deterministic source ordinal.</summary>
    public SimulationDataSource GetSource(int index)
        => _sources[index];

    /// <summary>Replace the base read options for this plan.</summary>
    public SimulationDataPlan WithReadOptions(ReplayReadOptions readOptions)
    {
        ArgumentNullException.ThrowIfNull(readOptions);
        ReadOptions = readOptions;
        return this;
    }

    /// <summary>Add a prepared simulation data source.</summary>
    public SimulationDataPlan AddSource(SimulationDataSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _sources.Add(source);
        return this;
    }

    /// <summary>Add an HPD replay source.</summary>
    public SimulationDataPlan AddSource(
        string sourceId,
        IReplaySource<FinanceEvent> source,
        int priority = 0,
        string sourceKind = "replay-source")
        => AddSource(new SimulationDataSource(sourceId, source, priority, sourceKind));

    /// <summary>Add an enumerable finance-event source.</summary>
    public SimulationDataPlan AddSource(
        string sourceId,
        IEnumerable<FinanceEvent> events,
        int priority = 0,
        string sourceKind = "enumerable")
        => AddSource(SimulationDataSource.FromEvents(sourceId, events, priority, sourceKind));

    /// <summary>Add an async finance-event source.</summary>
    public SimulationDataPlan AddSource(
        string sourceId,
        IAsyncEnumerable<FinanceEvent> events,
        int priority = 0,
        string sourceKind = "async-enumerable")
        => AddSource(SimulationDataSource.FromAsyncEvents(sourceId, events, priority, sourceKind));

    /// <summary>Add a source produced by a simulation catalog query.</summary>
    public SimulationDataPlan AddCatalogSource(
        string sourceId,
        ISimulationCatalog catalog,
        SimulationDataQuery query,
        int priority = 0,
        string sourceKind = "catalog")
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(query);
        return AddSource(sourceId, catalog.CreateReplaySource(query), priority, sourceKind);
    }
}
