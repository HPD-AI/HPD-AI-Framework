using Helium.Primitives;

namespace Helium.Algorithms;

/// <summary>
/// Finite simple undirected graph over exact ordered vertices.
/// </summary>
public readonly struct FiniteGraph<V> : IEquatable<FiniteGraph<V>>
    where V : notnull, ITotalOrder<V>
{
    public Finset<V> Vertices { get; }
    public Finset<UndirectedEdge<V>> Edges { get; }

    public FiniteGraph(Finset<V> vertices, Finset<UndirectedEdge<V>> edges)
    {
        foreach (var edge in edges.Elements)
        {
            if (!vertices.Contains(edge.A) || !vertices.Contains(edge.B))
                throw new ArgumentException("Every edge endpoint must be a graph vertex.", nameof(edges));
        }

        Vertices = vertices;
        Edges = edges;
    }

    public static FiniteGraph<V> Empty => new(Finset<V>.Empty, Finset<UndirectedEdge<V>>.Empty);

    public static FiniteGraph<V> FromVertices(IEnumerable<V> vertices) =>
        new(Finset<V>.FromElements(vertices), Finset<UndirectedEdge<V>>.Empty);

    public static FiniteGraph<V> FromEdges(IEnumerable<V> vertices, IEnumerable<UndirectedEdge<V>> edges) =>
        new(Finset<V>.FromElements(vertices), Finset<UndirectedEdge<V>>.FromElements(edges));

    public bool HasEdge(V a, V b) => Edges.Contains(new UndirectedEdge<V>(a, b));

    public int Degree(V vertex) => IncidentEdges(vertex).Card;

    public Finset<UndirectedEdge<V>> IncidentEdges(V vertex) =>
        Edges.Filter(edge => edge.Contains(vertex));

    public Finset<V> Neighbors(V vertex) =>
        IncidentEdges(vertex).Image(edge => edge.Other(vertex));

    public FiniteGraph<V> DeleteEdge(UndirectedEdge<V> edge) =>
        new(Vertices, Edges.Erase(edge));

    public FiniteGraph<V> ContractEdge(UndirectedEdge<V> edge)
    {
        var kept = edge.A;
        var removed = edge.B;
        var vertices = Vertices.Erase(removed);
        var edges = new List<UndirectedEdge<V>>();

        foreach (var existing in Edges.Elements)
        {
            if (existing == edge)
                continue;

            var a = V.DecidableEquals(existing.A, removed) ? kept : existing.A;
            var b = V.DecidableEquals(existing.B, removed) ? kept : existing.B;

            if (!V.DecidableEquals(a, b))
                edges.Add(new UndirectedEdge<V>(a, b));
        }

        return new FiniteGraph<V>(vertices, Finset<UndirectedEdge<V>>.FromElements(edges));
    }

    public bool Equals(FiniteGraph<V> other) =>
        Vertices.Equals(other.Vertices) && Edges.Equals(other.Edges);

    public override bool Equals(object? obj) => obj is FiniteGraph<V> other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Vertices, Edges);
    public static bool operator ==(FiniteGraph<V> left, FiniteGraph<V> right) => left.Equals(right);
    public static bool operator !=(FiniteGraph<V> left, FiniteGraph<V> right) => !left.Equals(right);
}
