using Rhodium.Primitives;

namespace Rhodium.Platform;

public readonly record struct VariantDescriptor(
    StrategyId StrategyId,
    int VariantIndex,
    ParameterSet Parameters);
