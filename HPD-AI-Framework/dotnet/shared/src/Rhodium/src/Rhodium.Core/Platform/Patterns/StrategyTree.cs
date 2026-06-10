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
            ChildIds = CopyChildren(children)
        };

        if (children != null)
        {
            foreach (var childId in children)
            {
                var childIdx = FindNodeIndex(childId);
                var (childStrategy, childNode) = _nodes[childIdx];
                _nodes[childIdx] = (childStrategy, childNode with { ParentId = id });
            }
        }

        _nodes.Add((strategy, node));
        return id;
    }

    public int NodeCount => _nodes.Count;

    public (Strategy Strategy, StrategyNode Node) GetNode(int index)
        => _nodes[index];

    public (Strategy Strategy, StrategyNode Node)[] GetNodesSnapshot()
    {
        var nodes = new (Strategy Strategy, StrategyNode Node)[_nodes.Count];
        for (var i = 0; i < _nodes.Count; i++)
            nodes[i] = _nodes[i];
        return nodes;
    }

    public IReadOnlyList<(Strategy Strategy, StrategyNode Node)> GetByDepth(int depth)
    {
        var count = 0;
        for (var i = 0; i < _nodes.Count; i++)
        {
            if (_nodes[i].Node.Depth == depth)
                count++;
        }

        var nodes = new (Strategy Strategy, StrategyNode Node)[count];
        var index = 0;
        for (var i = 0; i < _nodes.Count; i++)
        {
            if (_nodes[i].Node.Depth == depth)
                nodes[index++] = _nodes[i];
        }

        return nodes;
    }

    public int MaxDepth
    {
        get
        {
            var maxDepth = 0;
            for (var i = 0; i < _nodes.Count; i++)
            {
                if (_nodes[i].Node.Depth > maxDepth)
                    maxDepth = _nodes[i].Node.Depth;
            }

            return maxDepth;
        }
    }

    private void ValidateChildren(int parentDepth, IReadOnlyList<StrategyId> children)
    {
        var seen = new HashSet<StrategyId>();
        foreach (var childId in children)
        {
            if (!seen.Add(childId))
                throw new InvalidOperationException($"Strategy child '{childId}' is listed more than once.");

            var childIdx = FindNodeIndex(childId);
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

    private static ReadOnlyMemory<StrategyId> CopyChildren(IReadOnlyList<StrategyId>? children)
    {
        if (children is null || children.Count == 0)
            return ReadOnlyMemory<StrategyId>.Empty;

        var copied = new StrategyId[children.Count];
        for (var i = 0; i < children.Count; i++)
            copied[i] = children[i];

        return copied;
    }

    private int FindNodeIndex(StrategyId id)
    {
        for (var i = 0; i < _nodes.Count; i++)
        {
            if (_nodes[i].Node.Id == id)
                return i;
        }

        return -1;
    }
}
