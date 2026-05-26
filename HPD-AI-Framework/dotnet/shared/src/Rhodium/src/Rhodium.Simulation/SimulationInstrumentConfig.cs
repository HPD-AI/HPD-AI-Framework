using Rhodium.Primitives;

namespace Rhodium.Simulation;

/// <summary>
/// Instrument-scoped simulation configuration. Instrument settings override venue defaults.
/// </summary>
public sealed record SimulationInstrumentConfig
{
    /// <summary>Instrument represented by the simulated instrument engine.</summary>
    public required Instrument Instrument { get; init; }

    /// <summary>Canonical instrument contract used for valuation, accounting, margin, and fees.</summary>
    public required InstrumentContract Contract { get; init; }

    /// <summary>Instrument-specific simulation config. When unset, the venue config is used.</summary>
    public SimulationConfig? Config { get; init; }

    /// <summary>Instrument matching fidelity override.</summary>
    public MatchingFidelity? MatchingFidelity { get; init; }

    /// <summary>Initial trading status for this instrument.</summary>
    public MarketStatus? InitialStatus { get; init; }

    /// <summary>Instrument-specific order admission policy override.</summary>
    public SimulationOrderPolicy? OrderPolicy { get; init; }

    /// <summary>Instrument-specific execution behavior policy override.</summary>
    public SimulationVenuePolicy? SimulationPolicy { get; init; }

    /// <summary>Create an instrument configuration from a canonical instrument contract.</summary>
    public static SimulationInstrumentConfig For(InstrumentContract contract)
        => new() { Instrument = contract.Instrument, Contract = contract };
}
