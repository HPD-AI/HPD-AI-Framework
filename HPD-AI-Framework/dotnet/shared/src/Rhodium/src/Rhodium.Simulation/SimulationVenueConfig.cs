using Rhodium.Primitives;

namespace Rhodium.Simulation;

/// <summary>
/// Venue-scoped simulation configuration. Venue settings override run defaults.
/// </summary>
public sealed record SimulationVenueConfig
{
    /// <summary>Venue represented by the simulated exchange.</summary>
    public required Venue Venue { get; init; }

    /// <summary>Venue-specific starting cash. When unset, the run default is used.</summary>
    public Money? InitialCash { get; init; }

    /// <summary>Venue base currency for cash and account calculations.</summary>
    public Currency? BaseCurrency { get; init; }

    /// <summary>Venue account type. When unset, the simulation config account type is used.</summary>
    public AccountType? AccountType { get; init; }

    /// <summary>Venue-specific simulation config. When unset, the run default config is used.</summary>
    public SimulationConfig? Config { get; init; }

    /// <summary>Venue matching fidelity override.</summary>
    public MatchingFidelity? MatchingFidelity { get; init; }

    /// <summary>Venue order admission policy.</summary>
    public SimulationOrderPolicy OrderPolicy { get; init; } = SimulationOrderPolicy.Default;

    /// <summary>Venue execution behavior policy.</summary>
    public SimulationVenuePolicy SimulationPolicy { get; init; } = SimulationVenuePolicy.Default;

    /// <summary>Instrument engines configured under this venue.</summary>
    public IReadOnlyList<SimulationInstrumentConfig> InstrumentConfigs { get; init; } = [];

    /// <summary>Create a venue configuration for the supplied venue.</summary>
    public static SimulationVenueConfig For(Venue venue)
        => new() { Venue = venue };
}
