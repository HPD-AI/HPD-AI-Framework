using Rhodium.Kernel;
using Rhodium.Primitives;

namespace Rhodium.Platform.Patterns;

public sealed class StrategyTree
{
    private readonly List<(Strategy Strategy, StrategyNode Node)> _nodes = new();

    public StrategyId Register(
        Strategy strategy,
        int depth,
        IReadOnlyList<StrategyId>? children = null)
    {
        if (depth < 0)
            throw new ArgumentOutOfRangeException(nameof(depth), "Strategy depth cannot be negative.");

        if (children is not null)
            ValidateChildren(depth, children);

        var id = StrategyId.New();
        strategy.Id = id;
        strategy.Depth = depth;

        var node = new StrategyNode
        {
            Id = id,
            Depth = depth,
            ChildIds = children?.ToArray() ?? ReadOnlyMemory<StrategyId>.Empty
        };

        if (children != null)
        {
            foreach (var childId in children)
            {
                var childIdx = _nodes.FindIndex(n => n.Node.Id == childId);
                var (childStrategy, childNode) = _nodes[childIdx];
                _nodes[childIdx] = (childStrategy, childNode with { ParentId = id });
            }
        }

        _nodes.Add((strategy, node));
        return id;
    }

    public IReadOnlyList<(Strategy Strategy, StrategyNode Node)> Nodes => _nodes;

    public IReadOnlyList<(Strategy Strategy, StrategyNode Node)> GetByDepth(int depth)
        => _nodes.Where(n => n.Node.Depth == depth).ToArray();

    public int MaxDepth => _nodes.Count == 0 ? 0 : _nodes.Max(n => n.Node.Depth);

    private void ValidateChildren(int parentDepth, IReadOnlyList<StrategyId> children)
    {
        var seen = new HashSet<StrategyId>();
        foreach (var childId in children)
        {
            if (!seen.Add(childId))
                throw new InvalidOperationException($"Strategy child '{childId}' is listed more than once.");

            var childIdx = _nodes.FindIndex(n => n.Node.Id == childId);
            if (childIdx < 0)
                throw new InvalidOperationException($"Strategy child '{childId}' has not been registered.");

            var childNode = _nodes[childIdx].Node;
            if (childNode.Depth >= parentDepth)
            {
                throw new InvalidOperationException(
                    $"Strategy child '{childId}' depth {childNode.Depth} must be lower than parent depth {parentDepth}.");
            }

            if (childNode.ParentId.HasValue)
                throw new InvalidOperationException($"Strategy child '{childId}' already has parent '{childNode.ParentId.Value}'.");
        }
    }
}
