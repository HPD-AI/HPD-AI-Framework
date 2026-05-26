using HPD.Events;
using Rhodium.Primitives;
using Rhodium.Simulation.Frames;
using Rhodium.Simulation.Modules;

namespace Rhodium.Simulation;

/// <summary>
/// Run-level options for a simulation session.
/// </summary>
public sealed record SimulationRunOptions
{
    /// <summary>Default simulation configuration used when a venue does not override it.</summary>
    public SimulationConfig Config { get; init; } = SimulationConfig.Instant();

    /// <summary>Default exchange-owned matching fidelity for venues without overrides.</summary>
    public MatchingFidelity MatchingFidelity { get; init; } = MatchingFidelity.QueueAccurate;

    /// <summary>Default starting cash for venues without explicit cash configuration.</summary>
    public Money InitialCash { get; init; } = Money.USD(100_000m);

    /// <summary>Maximum strategy dispatch parallelism requested for the run.</summary>
    public int MaxDegreeOfParallelism { get; init; } = 1;

    /// <summary>Maximum same-timestamp replay-turn iterations before the session fails.</summary>
    public int MaxSameTimestampIterations { get; init; } = 128;

    /// <summary>Replay filtering and limiting options applied by session data readers.</summary>
    public ReplayReadOptions ReadOptions { get; init; } = ReplayReadOptions.All;

    /// <summary>Venue configurations participating in this run.</summary>
    public IReadOnlyList<SimulationVenueConfig> VenueConfigs { get; init; } = [];

    /// <summary>Explicit opening account state applied before the first replay event.</summary>
    public IReadOnlyList<AccountSeed> AccountSeeds { get; init; } = [];

    /// <summary>Session-scoped modules that observe and participate in replay turns.</summary>
    public IReadOnlyList<ISessionSimulationModule> SessionModules { get; init; } = [];

    /// <summary>Venue-scoped modules installed into each configured venue.</summary>
    public IReadOnlyList<IVenueSimulationModule> VenueModules { get; init; } = [];

    /// <summary>Instrument-scoped modules installed into configured instrument engines.</summary>
    public IReadOnlyList<IInstrumentSimulationModule> InstrumentModules { get; init; } = [];

    /// <summary>Controls whether struct frames are emitted in addition to object events.</summary>
    public SimulationFrameMode FrameMode { get; init; } = SimulationFrameMode.Disabled;
}
