using Rhodium.Primitives;

namespace Rhodium.Simulation.Data;

/// <summary>
/// Query for simulation-ready finance replay data.
/// </summary>
public sealed record SimulationDataQuery(
    IReadOnlyList<Instrument> Instruments,
    DateRange? Range = null,
    SimulationDataKind Kinds = SimulationDataKind.All)
{
    /// <summary>Query all instruments, all time ranges, and all event families.</summary>
    public static SimulationDataQuery All { get; } = new([]);

    /// <summary>Create a query for a single instrument.</summary>
    public static SimulationDataQuery ForInstrument(
        Instrument instrument,
        DateRange? range = null,
        SimulationDataKind kinds = SimulationDataKind.All)
        => new([instrument], range, kinds);
}
