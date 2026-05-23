using Rhodium.Analytics;
using Rhodium.Kernel;
using Rhodium.Platform;
using Rhodium.Primitives;

namespace Rhodium.Simulation;

public readonly record struct StrategyRunResult(
    StrategyId StrategyId,
    int VariantIndex,
    ParameterSet Parameters,
    TearSheet TearSheet,
    PortfolioSnapshot FinalSnapshot);
