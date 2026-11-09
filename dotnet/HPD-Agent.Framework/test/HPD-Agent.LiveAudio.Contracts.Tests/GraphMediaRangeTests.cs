using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphMediaRangeTests
{
    [Theory]
    [InlineData(1u)]
    [InlineData(GraphMediaRange.MaximumCount - 1)]
    [InlineData(GraphMediaRange.MaximumCount)]
    public void Accepted_bounds_preserve_exact_half_open_range(uint count)
    {
        var value = Range(count: count, encodedBytes: GraphMediaRange.MaximumEncodedBytes,
            duration: GraphMediaRange.MaximumMediaDurationNanoseconds);

        Assert.Equal(count, value.Count);
        Assert.Equal(10UL + count, value.EndExclusive.Value);
    }

    [Fact]
    public void Invalid_numeric_bounds_fail_closed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Range(count: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Range(count: GraphMediaRange.MaximumCount + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Range(encodedBytes: GraphMediaRange.MaximumEncodedBytes + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Range(duration: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Range(duration: GraphMediaRange.MaximumMediaDurationNanoseconds + 1));
    }

    [Fact]
    public void End_overflow_fails_closed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Range(start: ulong.MaxValue, count: 1));
    }

    [Fact]
    public void Unknown_direction_and_domain_fail_closed()
    {
        Assert.Throws<ArgumentException>(() => Range(direction: 0));
        Assert.Throws<ArgumentException>(() => Range(domain: 0));
        Assert.Throws<ArgumentException>(() => Range(direction: (GraphDirection)5));
        Assert.Throws<ArgumentException>(() => Range(domain: (GraphTrafficDomain)6));
    }

    [Fact]
    public void Default_authority_identifiers_fail_closed()
    {
        Assert.Throws<ArgumentException>(() => new GraphMediaRange(default, GraphGenerationId.Create(),
            GraphDirection.IngressForward, GraphTrafficDomain.Media, new(0), 1, 0, new(0)));
        Assert.Throws<ArgumentException>(() => new GraphMediaRange(Session(), default,
            GraphDirection.IngressForward, GraphTrafficDomain.Media, new(0), 1, 0, new(0)));
        Assert.False(default(GraphMediaRange).IsValid);
        Assert.False(default(GraphMediaRange).HasSameScope(default));
        Assert.False(default(GraphMediaRange).IsImmediatelyBefore(default));
    }

    [Fact]
    public void Adjacency_requires_exact_scope_and_position()
    {
        var session = Session();
        var generation = GraphGenerationId.Create();
        var first = Range(session, generation, start: 10, count: 5);
        var adjacent = Range(session, generation, start: 15, count: 2);

        Assert.True(first.IsImmediatelyBefore(adjacent));
        Assert.False(first.IsImmediatelyBefore(Range(session, generation, start: 16, count: 2)));
        Assert.False(first.IsImmediatelyBefore(Range(Session(), generation, start: 15, count: 2)));
        Assert.False(first.IsImmediatelyBefore(Range(session, GraphGenerationId.Create(), start: 15, count: 2)));
        Assert.False(first.IsImmediatelyBefore(Range(session, generation, start: 15, count: 2,
            direction: GraphDirection.EgressForward)));
        Assert.False(first.IsImmediatelyBefore(Range(session, generation, start: 15, count: 2,
            domain: GraphTrafficDomain.Evidence)));
    }

    private static GraphMediaRange Range(uint count = 1, ulong start = 10, ulong encodedBytes = 0,
        long duration = 0, GraphDirection direction = GraphDirection.IngressForward,
        GraphTrafficDomain domain = GraphTrafficDomain.Media) =>
        Range(Session(), GraphGenerationId.Create(), count, start, encodedBytes, duration, direction, domain);

    private static GraphMediaRange Range(SessionAuthorityStampV1 session, GraphGenerationId generation,
        uint count = 1, ulong start = 10, ulong encodedBytes = 0, long duration = 0,
        GraphDirection direction = GraphDirection.IngressForward,
        GraphTrafficDomain domain = GraphTrafficDomain.Media) =>
        new(session, generation, direction, domain, new(start), count, encodedBytes, new(duration));

    private static SessionAuthorityStampV1 Session() => new(RuntimeGenerationId.Create(), LiveSessionId.Create());
}
