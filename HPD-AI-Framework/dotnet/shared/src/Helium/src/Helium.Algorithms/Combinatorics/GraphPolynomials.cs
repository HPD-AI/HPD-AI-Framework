using Helium.Algebra;
using Helium.Primitives;

namespace Helium.Algorithms;

public static class GraphPolynomials
{
    /// <summary>
    /// Chromatic polynomial via exact deletion-contraction.
    /// Intended for small finite graphs.
    /// </summary>
    public static SparsePolynomial<Integer> ChromaticPolynomial<V>(FiniteGraph<V> graph)
        where V : notnull, ITotalOrder<V>
    {
        if (graph.Edges.IsEmpty)
            return SparsePolynomial<Integer>.Monomial(graph.Vertices.Card, Integer.One);

        var edge = graph.Edges.Elements.First();
        return ChromaticPolynomial(graph.DeleteEdge(edge)) - ChromaticPolynomial(graph.ContractEdge(edge));
    }
}
