using Rhodium.Kernel;
using Rhodium.Primitives;

namespace Rhodium.Control;

public enum PositionTransitionKind
{
    None,
    Opened,
    Changed,
    Closed
}

public readonly struct PositionTransition
{
    public StrategyId StrategyId { get; init; }
    public AssetId AssetId { get; init; }
    public PositionTransitionKind Kind { get; init; }
    public PositionState Previous { get; init; }
    public PositionState Current { get; init; }
}
