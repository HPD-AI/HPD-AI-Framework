using HPD.Agent.Audio.Graph;
using HPD.Agent.Authority;

namespace HPD.Agent.LiveAudio.Contracts.Tests;

public sealed class GraphMediaRangeV1Tests
{
    [Theory]
    [InlineData(1u)]
    [InlineData(GraphMediaRangeV1.MaximumCount - 1)]
    [InlineData(GraphMediaRangeV1.MaximumCount)]
    public void Accepted_bounds_preserve_exact_half_open_range(uint count)
    {
        var value = Range(count: count, encodedBytes: GraphMediaRangeV1.MaximumEncodedBytes,
            duration: GraphMediaRangeV1.MaximumMediaDurationNanoseconds);

        Assert.Equal(count, value.Count);
        Assert.Equal(10UL + count, value.EndExclusive.Value);
    }

    [Fact]
    public void Invalid_numeric_bounds_fail_closed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Range(count: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Range(count: GraphMediaRangeV1.MaximumCount + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Range(encodedBytes: GraphMediaRangeV1.MaximumEncodedBytes + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Range(duration: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Range(duration: GraphMediaRangeV1.MaximumMediaDurationNanoseconds + 1));
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
        Assert.Throws<ArgumentException>(() => Range(direction: (GraphDirectionV1)5));
        Assert.Throws<ArgumentException>(() => Range(domain: (GraphTrafficDomainV1)6));
    }

    [Fact]
    public void Default_authority_identifiers_fail_closed()
    {
        Assert.Throws<ArgumentException>(() => new GraphMediaRangeV1(default, GraphGenerationId.Create(),
            GraphDirectionV1.IngressForward, GraphTrafficDomainV1.Media, new(0), 1, 0, new(0)));
        Assert.Throws<ArgumentException>(() => new GraphMediaRangeV1(Session(), default,
            GraphDirectionV1.IngressForward, GraphTrafficDomainV1.Media, new(0), 1, 0, new(0)));
        Assert.False(default(GraphMediaRangeV1).IsValid);
        Assert.False(default(GraphMediaRangeV1).HasSameScope(default));
        Assert.False(default(GraphMediaRangeV1).IsImmediatelyBefore(default));
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
            direction: GraphDirectionV1.EgressForward)));
        Assert.False(first.IsImmediatelyBefore(Range(session, generation, start: 15, count: 2,
            domain: GraphTrafficDomainV1.Evidence)));
    }

    private static GraphMediaRangeV1 Range(uint count = 1, ulong start = 10, ulong encodedBytes = 0,
        long duration = 0, GraphDirectionV1 direction = GraphDirectionV1.IngressForward,
        GraphTrafficDomainV1 domain = GraphTrafficDomainV1.Media) =>
        Range(Session(), GraphGenerationId.Create(), count, start, encodedBytes, duration, direction, domain);

    private static GraphMediaRangeV1 Range(SessionAuthorityStampV1 session, GraphGenerationId generation,
        uint count = 1, ulong start = 10, ulong encodedBytes = 0, long duration = 0,
        GraphDirectionV1 direction = GraphDirectionV1.IngressForward,
        GraphTrafficDomainV1 domain = GraphTrafficDomainV1.Media) =>
        new(session, generation, direction, domain, new(start), count, encodedBytes, new(duration));

    private static SessionAuthorityStampV1 Session() => new(RuntimeGenerationId.Create(), LiveSessionId.Create());
}
