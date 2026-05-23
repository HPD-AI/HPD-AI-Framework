using Helium.Primitives;

namespace Helium.Algorithms;

/// <summary>
/// Normalized undirected edge between two distinct ordered vertices.
/// </summary>
public readonly struct UndirectedEdge<V> :
    IEquatable<UndirectedEdge<V>>,
    IDecidableEq<UndirectedEdge<V>>,
    ITotalOrder<UndirectedEdge<V>>
    where V : notnull, ITotalOrder<V>
{
    public V A { get; }
    public V B { get; }

    public UndirectedEdge(V a, V b)
    {
        if (V.DecidableEquals(a, b))
            throw new ArgumentException("Self-loops are not valid finite simple graph edges.", nameof(b));

        if (V.CompareOrder(a, b) == Ordering.Greater)
        {
            A = b;
            B = a;
        }
        else
        {
            A = a;
            B = b;
        }
    }

    public bool Contains(V vertex) =>
        V.DecidableEquals(A, vertex) || V.DecidableEquals(B, vertex);

    public V Other(V vertex)
    {
        if (V.DecidableEquals(A, vertex)) return B;
        if (V.DecidableEquals(B, vertex)) return A;
        throw new ArgumentException("Vertex is not incident to this edge.", nameof(vertex));
    }

    public static bool DecidableEquals(UndirectedEdge<V> left, UndirectedEdge<V> right) => left == right;

    public static bool LessEqual(UndirectedEdge<V> left, UndirectedEdge<V> right) =>
        CompareOrder(left, right) != Ordering.Greater;

    public static Ordering CompareOrder(UndirectedEdge<V> left, UndirectedEdge<V> right)
    {
        var aCompare = V.CompareOrder(left.A, right.A);
        return aCompare != Ordering.Equal ? aCompare : V.CompareOrder(left.B, right.B);
    }

    public bool Equals(UndirectedEdge<V> other) =>
        V.DecidableEquals(A, other.A) && V.DecidableEquals(B, other.B);

    public override bool Equals(object? obj) => obj is UndirectedEdge<V> other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(A, B);
    public static bool operator ==(UndirectedEdge<V> left, UndirectedEdge<V> right) => left.Equals(right);
    public static bool operator !=(UndirectedEdge<V> left, UndirectedEdge<V> right) => !left.Equals(right);
    public override string ToString() => $"{{{A}, {B}}}";
}
