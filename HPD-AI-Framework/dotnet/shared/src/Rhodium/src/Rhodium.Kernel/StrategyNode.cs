using Rhodium.Primitives;

namespace Rhodium.Kernel;

public readonly struct StrategyNode
{
    public StrategyId Id { get; init; }
    public StrategyId? ParentId { get; init; }
    public ReadOnlyMemory<StrategyId> ChildIds { get; init; }
    public int Depth { get; init; }

    public bool IsLeaf => ChildIds.IsEmpty;
    public bool IsRoot => !ParentId.HasValue;
}
