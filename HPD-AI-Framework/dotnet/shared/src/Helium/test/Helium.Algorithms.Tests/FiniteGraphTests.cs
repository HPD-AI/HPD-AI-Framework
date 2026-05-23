using Helium.Algebra;
using Helium.Algorithms;
using Helium.Primitives;

namespace Helium.Algorithms.Tests;

public class FiniteGraphTests
{
    [Fact]
    public void UndirectedEdge_NormalizesEndpointOrder()
    {
        Assert.Equal(
            new UndirectedEdge<Fin>(V(0, 3), V(2, 3)),
            new UndirectedEdge<Fin>(V(2, 3), V(0, 3)));
    }

    [Fact]
    public void UndirectedEdge_RejectsLoops()
    {
        Assert.Throws<ArgumentException>(() => new UndirectedEdge<Fin>(V(1, 3), V(1, 3)));
    }

    [Fact]
    public void FiniteGraph_RejectsEdgesOutsideVertexSet()
    {
        var vertices = Finset<Fin>.Of(V(0, 3), V(1, 3));
        var edges = Finset<UndirectedEdge<Fin>>.Of(E(0, 2, 3));

        Assert.Throws<ArgumentException>(() => new FiniteGraph<Fin>(vertices, edges));
    }

    [Fact]
    public void DegreeAndNeighbors()
    {
        var graph = Path3();

        Assert.Equal(2, graph.Degree(V(1, 3)));
        Assert.Equal(Finset<Fin>.Of(V(0, 3), V(2, 3)), graph.Neighbors(V(1, 3)));
    }

    [Fact]
    public void BfsAndDfs_VisitConnectedComponent()
    {
        var graph = Path3();

        Assert.Equal([V(0, 3), V(1, 3), V(2, 3)], GraphAlgorithms.Bfs(graph, V(0, 3)));
        Assert.Equal([V(0, 3), V(1, 3), V(2, 3)], GraphAlgorithms.Dfs(graph, V(0, 3)));
    }

    [Fact]
    public void ConnectedComponents_DetectsDisconnectedGraph()
    {
        var graph = new FiniteGraph<Fin>(
            Finset<Fin>.Of(V(0, 4), V(1, 4), V(2, 4), V(3, 4)),
            Finset<UndirectedEdge<Fin>>.Of(E(0, 1, 4), E(2, 3, 4)));

        var components = GraphAlgorithms.ConnectedComponents(graph);

        Assert.False(GraphAlgorithms.IsConnected(graph));
        Assert.Equal(2, components.Count);
        Assert.Contains(Finset<Fin>.Of(V(0, 4), V(1, 4)), components);
        Assert.Contains(Finset<Fin>.Of(V(2, 4), V(3, 4)), components);
    }

    [Fact]
    public void Eulerian_Check()
    {
        Assert.False(GraphAlgorithms.IsEulerian(Path3()));
        Assert.True(GraphAlgorithms.IsEulerian(Cycle3()));
    }

    [Fact]
    public void Bipartite_Check()
    {
        Assert.True(GraphAlgorithms.IsBipartite(Path3()));
        Assert.False(GraphAlgorithms.IsBipartite(Cycle3()));
    }

    [Fact]
    public void DeleteAndContractEdge()
    {
        var graph = Path3();
        var edge = E(0, 1, 3);

        Assert.False(graph.DeleteEdge(edge).Edges.Contains(edge));

        var contracted = graph.ContractEdge(edge);
        Assert.Equal(2, contracted.Vertices.Card);
        Assert.True(contracted.HasEdge(V(0, 3), V(2, 3)));
    }

    [Fact]
    public void ChromaticPolynomial_Edgeless3()
    {
        var graph = FiniteGraph<Fin>.FromVertices([V(0, 3), V(1, 3), V(2, 3)]);

        Assert.Equal(P(0, 0, 0, 1), GraphPolynomials.ChromaticPolynomial(graph));
    }

    [Fact]
    public void ChromaticPolynomial_Path3()
    {
        Assert.Equal(P(0, 1, -2, 1), GraphPolynomials.ChromaticPolynomial(Path3()));
    }

    [Fact]
    public void ChromaticPolynomial_Cycle3()
    {
        Assert.Equal(P(0, 2, -3, 1), GraphPolynomials.ChromaticPolynomial(Cycle3()));
    }

    private static Fin V(int value, int bound) => new(value, bound);

    private static UndirectedEdge<Fin> E(int a, int b, int bound) => new(V(a, bound), V(b, bound));

    private static FiniteGraph<Fin> Path3() =>
        new(
            Finset<Fin>.Of(V(0, 3), V(1, 3), V(2, 3)),
            Finset<UndirectedEdge<Fin>>.Of(E(0, 1, 3), E(1, 2, 3)));

    private static FiniteGraph<Fin> Cycle3() =>
        new(
            Finset<Fin>.Of(V(0, 3), V(1, 3), V(2, 3)),
            Finset<UndirectedEdge<Fin>>.Of(E(0, 1, 3), E(1, 2, 3), E(0, 2, 3)));

    private static SparsePolynomial<Integer> P(params int[] coefficients)
    {
        var values = coefficients.Select(coefficient => (Integer)coefficient).ToArray();
        return SparsePolynomial<Integer>.FromCoeffs(values);
    }
}
