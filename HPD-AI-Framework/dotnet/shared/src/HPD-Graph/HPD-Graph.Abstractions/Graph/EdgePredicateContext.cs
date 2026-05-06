using HPDAgent.Graph.Abstractions.Context;

namespace HPDAgent.Graph.Abstractions.Graph;

/// <summary>
/// Runtime-only context for non-serializable edge predicates.
/// </summary>
public sealed class EdgePredicateContext
{
    public EdgePredicateContext(
        IGraphContext graphContext,
        Edge edge,
        IReadOnlyDictionary<string, object>? sourceOutputs)
    {
        GraphContext = graphContext;
        Edge = edge;
        SourceOutputs = sourceOutputs ?? new Dictionary<string, object>();
    }

    public IGraphContext GraphContext { get; }

    public Edge Edge { get; }

    public IReadOnlyDictionary<string, object> SourceOutputs { get; }

    public T? Get<T>(string key)
    {
        return SourceOutputs.TryGetValue(key, out var value) && value is T typedValue
            ? typedValue
            : default;
    }

    public bool HasKey(string key)
    {
        return SourceOutputs.ContainsKey(key);
    }
}
