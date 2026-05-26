using Rhodium.Analytics;
using Rhodium.Kernel;
using Rhodium.Platform;
using Rhodium.Primitives;

namespace Rhodium.Simulation;

/// <summary>
/// Final per-strategy, per-variant result produced by a simulation session.
/// </summary>
/// <param name="StrategyId">Strategy identity for the run.</param>
/// <param name="VariantIndex">Variant index within the strategy grid.</param>
/// <param name="Parameters">Parameter values used by this variant.</param>
/// <param name="TearSheet">Performance summary for the variant.</param>
/// <param name="FinalSnapshot">Final strategy-local portfolio snapshot.</param>
public readonly record struct StrategyRunResult(
    StrategyId StrategyId,
    int VariantIndex,
    ParameterSet Parameters,
    TearSheet TearSheet,
    PortfolioSnapshot FinalSnapshot);
