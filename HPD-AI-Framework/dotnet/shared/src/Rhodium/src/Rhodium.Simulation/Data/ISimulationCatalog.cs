using HPD.Events;
using Rhodium.Events;
using Rhodium.Primitives;

namespace Rhodium.Simulation.Data;

/// <summary>
/// Simulation-facing catalog for replayable finance data.
/// </summary>
public interface ISimulationCatalog
{
    /// <summary>Create a replay source matching the supplied simulation data query.</summary>
    IReplaySource<FinanceEvent> CreateReplaySource(SimulationDataQuery query);

    /// <summary>Enumerate instruments known to this catalog.</summary>
    IAsyncEnumerable<Instrument> ListInstrumentsAsync(CancellationToken ct = default);

    /// <summary>Get the available replay time range for one instrument and data shape.</summary>
    Task<DateRange?> GetAvailableRangeAsync<T>(
        Instrument instrument,
        CancellationToken ct = default);
}
