using Helium.Algorithms;
using Helium.Primitives;

namespace Helium.Algorithms.Tests;

public class FiniteTopologyTests
{
    [Fact]
    public void DiscreteTopology_HasEverySubsetOpen()
    {
        var points = Points2();
        var topology = FiniteTopology<Fin>.Discrete(points);

        Assert.Equal(4, topology.OpenSets.Card);
        Assert.True(topology.IsOpen(Finset<Fin>.Of(P(0), P(1))));
        Assert.True(topology.IsOpen(Finset<Fin>.Of(P(0))));
        Assert.True(topology.IsOpen(Finset<Fin>.Of(P(1))));
    }

    [Fact]
    public void IndiscreteTopology_HasOnlyEmptyAndFullOpen()
    {
        var points = Points2();
        var topology = FiniteTopology<Fin>.Indiscrete(points);

        Assert.Equal(2, topology.OpenSets.Card);
        Assert.True(topology.IsOpen(Finset<Fin>.Empty));
        Assert.True(topology.IsOpen(points));
        Assert.False(topology.IsOpen(Finset<Fin>.Of(P(0))));
    }

    [Fact]
    public void Constructor_RejectsInvalidOpenSetFamily()
    {
        var points = Points2();
        var invalid = Opens(Finset<Fin>.Empty, Finset<Fin>.Of(P(0)));

        Assert.Throws<ArgumentException>(() => new FiniteTopology<Fin>(points, invalid));
    }

    [Fact]
    public void Constructor_RejectsOpenSetOutsidePointSet()
    {
        var points = Finset<Fin>.Of(new Fin(0, 3), new Fin(1, 3));
        var invalid = Opens(Finset<Fin>.Empty, points, Finset<Fin>.Of(new Fin(2, 3)));

        Assert.Throws<ArgumentException>(() => new FiniteTopology<Fin>(points, invalid));
    }

    [Fact]
    public void SierpinskiTopology_ComputesInteriorClosureAndBoundary()
    {
        var topology = Sierpinski();
        var closedPoint = Finset<Fin>.Of(P(0));
        var openPoint = Finset<Fin>.Of(P(1));

        Assert.True(topology.IsClosed(closedPoint));
        Assert.Equal(Finset<Fin>.Empty, topology.Interior(closedPoint));
        Assert.Equal(closedPoint, topology.Closure(closedPoint));
        Assert.Equal(closedPoint, topology.Boundary(closedPoint));

        Assert.True(topology.IsOpen(openPoint));
        Assert.Equal(openPoint, topology.Interior(openPoint));
        Assert.Equal(Points2(), topology.Closure(openPoint));
        Assert.Equal(Finset<Fin>.Of(P(0)), topology.Boundary(openPoint));
    }

    [Fact]
    public void Connectedness_DistinguishesDiscreteAndSierpinskiSpaces()
    {
        Assert.False(FiniteTopology<Fin>.Discrete(Points2()).IsConnected());
        Assert.True(Sierpinski().IsConnected());
    }

    [Fact]
    public void Continuity_UsesPreimagesOfOpenSets()
    {
        var discrete = FiniteTopology<Fin>.Discrete(Points2());
        var indiscrete = FiniteTopology<Fin>.Indiscrete(Points2());

        Assert.True(FiniteTopology<Fin>.IsContinuous(discrete, Sierpinski(), x => x));
        Assert.False(FiniteTopology<Fin>.IsContinuous(indiscrete, Sierpinski(), x => x));
    }

    private static Fin P(int value) => new(value, 2);

    private static Finset<Fin> Points2() => Finset<Fin>.Of(P(0), P(1));

    private static Finset<Finset<Fin>> Opens(params Finset<Fin>[] openSets) =>
        Finset<Finset<Fin>>.FromElements(openSets);

    private static FiniteTopology<Fin> Sierpinski()
    {
        var points = Points2();
        return new FiniteTopology<Fin>(
            points,
            Opens(Finset<Fin>.Empty, Finset<Fin>.Of(P(1)), points));
    }
}
