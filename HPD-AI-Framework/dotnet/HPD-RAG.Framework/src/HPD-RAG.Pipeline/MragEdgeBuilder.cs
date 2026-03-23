using HPDAgent.Graph.Abstractions.Graph;

namespace HPD.RAG.Pipeline;

/// <summary>
/// Fluent builder for wiring edges between nodes in a <see cref="MragPipeline"/>.
/// Obtained by calling <c>MragPipeline.From(...)</c>.
///
/// Typical usage:
/// <code>
/// pipeline
///     .From("START").To("read").To("chunk").To("write").To("END").Done()
///     .From("router").Port(0).To("text-branch").Done()
///     .From("router").Port(1).To("image-branch").Done();
/// </code>
/// </summary>
public sealed class MragEdgeBuilder
{
    private readonly MragPipeline _pipeline;
    private string[] _sourceNodes;
    private int? _fromPort;
    private EdgeCondition? _condition;

    internal MragEdgeBuilder(MragPipeline pipeline, string[] sourceNodes)
    {
        _pipeline = pipeline;
        _sourceNodes = sourceNodes;
    }

    // ------------------------------------------------------------------ //
    // Source manipulation                                                  //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Start a new edge chain from a different set of source nodes,
    /// returning <c>this</c> builder (reuses the same builder instance
    /// so the caller can chain without calling <see cref="Done"/> first).
    /// </summary>
    public MragEdgeBuilder From(params string[] sourceNodes)
    {
        if (sourceNodes == null || sourceNodes.Length == 0)
            throw new ArgumentException("At least one source node is required.", nameof(sourceNodes));

        // Flush any pending edges before resetting source
        _sourceNodes = sourceNodes;
        _fromPort = null;
        _condition = null;
        return this;
    }

    // ------------------------------------------------------------------ //
    // Port / condition configuration                                       //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Specifies which output port (0-indexed) the edges in this chain originate from.
    /// Maps to <c>EdgeBuilder.FromPort(int)</c> on HPD.Graph.
    /// Required when the source node has multiple output ports (router nodes).
    /// </summary>
    public MragEdgeBuilder Port(int port)
    {
        if (port < 0)
            throw new ArgumentOutOfRangeException(nameof(port), "Port number must be non-negative.");
        _fromPort = port;
        return this;
    }

    /// <summary>
    /// Adds a <c>FieldEquals</c> edge condition: the edge is only traversed when
    /// <paramref name="field"/> in the source node's output dictionary equals <paramref name="value"/>.
    /// </summary>
    public MragEdgeBuilder WhenEquals(string field, object value)
    {
        if (string.IsNullOrWhiteSpace(field))
            throw new ArgumentException("Field name cannot be empty.", nameof(field));
        ArgumentNullException.ThrowIfNull(value);

        _condition = new EdgeCondition
        {
            Type = ConditionType.FieldEquals,
            Field = field,
            Value = value
        };
        return this;
    }

    /// <summary>
    /// Marks the edge as the default route for a router node.
    /// Sets <c>ConditionType.Always</c> so the edge is always traversed
    /// unless a higher-priority conditional edge fires first.
    /// </summary>
    public MragEdgeBuilder AsDefault()
    {
        _condition = new EdgeCondition { Type = ConditionType.Always };
        return this;
    }

    // ------------------------------------------------------------------ //
    // Target specification                                                 //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Adds edges from all current source nodes to <paramref name="targetNode"/>
    /// and returns <c>this</c> builder so additional targets can be chained:
    /// <code>
    /// pipeline.From("START").To("a").To("b").To("END").Done()
    /// </code>
    /// </summary>
    public MragEdgeBuilder To(string targetNode)
    {
        if (string.IsNullOrWhiteSpace(targetNode))
            throw new ArgumentException("Target node ID cannot be empty.", nameof(targetNode));

        int? capturedPort = _fromPort;
        EdgeCondition? capturedCondition = _condition;

        foreach (var source in _sourceNodes)
        {
            _pipeline.AddEdgeInternal(source, targetNode, capturedPort, capturedCondition);
        }

        // After adding edges, the target becomes the new source for chained .To() calls.
        // Port and condition are reset — each hop in a linear chain is unconditional
        // unless the caller calls .Port() or .WhenEquals() again.
        _sourceNodes = new[] { targetNode };
        _fromPort = null;
        _condition = null;

        return this;
    }

    // ------------------------------------------------------------------ //
    // Termination                                                          //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Returns the parent <see cref="MragPipeline"/> for further chaining.
    /// </summary>
    public MragPipeline Done() => _pipeline;
}
