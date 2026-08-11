using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphTopologyPlanV1Tests
{
    [Fact]
    public void Canonical_order_is_input_independent_and_has_fixed_golden()
    {
        var first = Plan([Node("sink"), Node("source"), Node("middle")],
            [Edge("middle", "sink"), Edge("source", "middle")], [new(4), new(1)]);
        var second = Plan([Node("middle"), Node("source"), Node("sink")],
            [Edge("source", "middle"), Edge("middle", "sink")], [new(1), new(4)]);

        Assert.Equal(["middle", "sink", "source"], first.Nodes.Select(node => node.Key.ToString()));
        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal("8f9cf9623ca162386049aa88eec3a143d67d3fc00d699f6c52caf61d1449643b",
            first.Fingerprint.ToString());
    }

    [Fact]
    public void Construction_owns_mutable_inputs()
    {
        var nodes = new List<GraphTopologyNodeV1> { Node("source"), Node("sink") };
        var edges = new List<GraphTopologyEdgeV1> { Edge("source", "sink") };
        var dimensions = new List<CapacityDimensionId> { new(1) };
        var plan = Plan(nodes, edges, dimensions);

        nodes.Clear(); edges.Clear(); dimensions.Clear();

        Assert.Equal(2, plan.Nodes.Count);
        Assert.Single(plan.Edges);
        Assert.Single(plan.CapacityDimensions);
        Assert.False(plan.Nodes is GraphTopologyNodeV1[]);
        Assert.False(plan.Edges is GraphTopologyEdgeV1[]);
        Assert.False(plan.CapacityDimensions is CapacityDimensionId[]);
    }

    [Fact]
    public void Node_and_edge_bounds_are_exact()
    {
        Assert.NotNull(Plan(Enumerable.Range(0, 64).Select(index => Node($"n{index:00}")), [], [new(1)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Plan(
            Enumerable.Range(0, 65).Select(index => Node($"n{index:00}")), [], [new(1)]));

        var nodes = Enumerable.Range(0, 24).Select(index => Node($"n{index:00}")).ToArray();
        var edges = (from source in Enumerable.Range(0, 24)
                     from target in Enumerable.Range(source + 1, 23 - source)
                     select Edge($"n{source:00}", $"n{target:00}")).ToArray();
        Assert.NotNull(Plan(nodes, edges.Take(256), [new(1)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Plan(nodes, edges.Take(257), [new(1)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Plan(HostileUnboundedNodes(), [], [new(1)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Plan([Node("a"), Node("b")], HostileUnboundedEdges(), [new(1)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => Plan([Node("a")], [], HostileUnboundedDimensions()));
    }

    [Fact]
    public void Duplicate_missing_self_and_cyclic_edges_fail_closed()
    {
        Assert.Throws<ArgumentException>(() => Plan([Node("a"), Node("a")], [], [new(1)]));
        Assert.Throws<ArgumentException>(() => Plan([Node("a")], [Edge("a", "missing")], [new(1)]));
        Assert.Throws<ArgumentException>(() => Edge("a", "a"));
        Assert.Throws<ArgumentException>(() => Plan([Node("a"), Node("b")],
            [Edge("a", "b"), Edge("a", "b")], [new(1)]));
        Assert.Throws<ArgumentException>(() => Plan([Node("a"), Node("b")],
            [Edge("a", "b"), Edge("b", "a")], [new(1)]));
    }

    [Fact]
    public void Capacity_dimensions_are_required_registered_and_unique()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Plan([Node("a")], [], []));
        Assert.Throws<ArgumentException>(() => Plan([Node("a")], [], [new(1), new(1)]));
        Assert.Throws<ArgumentException>(() => Plan([Node("a")], [], [default]));
    }

    [Fact]
    public void Keys_are_printable_ascii_without_spaces_and_at_most_64_bytes()
    {
        Assert.Throws<ArgumentException>(() => Node("has space"));
        Assert.Throws<ArgumentException>(() => Node("line\nbreak"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Node(new string('a', 65)));
        Assert.NotNull(Node(new string('a', 64)));
    }

    [Fact]
    public void Default_authority_values_fail_closed()
    {
        Assert.Throws<ArgumentException>(() => new GraphTopologyPlanV1(default, GraphGenerationId.Create(),
            CapacityGrantId.Create(), [Node("a")], [], [new(1)]));
        Assert.Throws<ArgumentException>(() => new GraphTopologyPlanV1(Session(), default,
            CapacityGrantId.Create(), [Node("a")], [], [new(1)]));
        Assert.Throws<ArgumentException>(() => new GraphTopologyPlanV1(Session(), GraphGenerationId.Create(),
            default, [Node("a")], [], [new(1)]));
    }

    private static GraphTopologyPlanV1 Plan(IEnumerable<GraphTopologyNodeV1> nodes,
        IEnumerable<GraphTopologyEdgeV1> edges, IEnumerable<CapacityDimensionId> dimensions) =>
        new(Session(), Graph(), Grant(), nodes, edges, dimensions);

    private static GraphTopologyNodeV1 Node(string key) => new(new BoundedAscii(key));
    private static GraphTopologyEdgeV1 Edge(string source, string target) => new(new(source), new(target));
    private static SessionAuthorityStampV1 Session() => new(
        RuntimeGenerationId.FromValue(Id(1)), LiveSessionId.FromValue(Id(2)));
    private static GraphGenerationId Graph() => GraphGenerationId.FromValue(Id(3));
    private static CapacityGrantId Grant() => CapacityGrantId.FromValue(Id(4));
    private static StableId128 Id(byte seed)
    {
        Span<byte> bytes = stackalloc byte[16];
        for (var index = 0; index < bytes.Length; index++) bytes[index] = checked((byte)(seed + index));
        return StableId128.FromBytes(bytes);
    }

    private static IEnumerable<GraphTopologyNodeV1> HostileUnboundedNodes()
    {
        for (var index = 0; index <= GraphTopologyPlanV1.MaximumNodes; index++)
            yield return Node($"h{index:00}");
        throw new InvalidOperationException("The bounded collector read beyond max+1.");
    }

    private static IEnumerable<GraphTopologyEdgeV1> HostileUnboundedEdges()
    {
        for (var index = 0; index <= GraphTopologyPlanV1.MaximumEdges; index++)
            yield return Edge($"a{index:000}", $"b{index:000}");
        throw new InvalidOperationException("The bounded collector read beyond max+1.");
    }

    private static IEnumerable<CapacityDimensionId> HostileUnboundedDimensions()
    {
        for (var index = 0; index < 15; index++)
            yield return new CapacityDimensionId(checked((ushort)(index % 14 + 1)));
        throw new InvalidOperationException("The bounded collector read beyond max+1.");
    }
}
