using Rhodium.Primitives;

namespace Rhodium.Simulation;

/// <summary>
/// Explicit opening account state for a simulation run.
/// </summary>
public sealed record AccountSeed(
    Venue Venue,
    IReadOnlyList<Money> Cash,
    IReadOnlyList<SeedPosition> Positions,
    StrategyId? StrategyId = null,
    int VariantId = 0,
    string? ExternalReference = null);

/// <summary>
/// Explicit opening custody position for a simulation account seed.
/// </summary>
public sealed record SeedPosition(
    Instrument Instrument,
    Qty Quantity,
    Price CarryingPrice,
    string? ExternalReference = null);
