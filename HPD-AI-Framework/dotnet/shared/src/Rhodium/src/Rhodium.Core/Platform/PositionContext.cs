using Rhodium.Control;
using Rhodium.Kernel;
using Rhodium.Primitives;

namespace Rhodium.Platform;

public readonly ref struct PositionContext
{
    internal PositionContext(PositionTransition transition)
    {
        StrategyId = transition.StrategyId;
        AssetId = transition.AssetId;
        Kind = transition.Kind;
        Previous = transition.Previous;
        Current = transition.Current;
    }

    public StrategyId StrategyId { get; }
    public AssetId AssetId { get; }
    public PositionTransitionKind Kind { get; }
    public PositionState Previous { get; }
    public PositionState Current { get; }
}
