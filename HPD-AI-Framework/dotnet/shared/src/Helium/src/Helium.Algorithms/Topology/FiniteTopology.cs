using Helium.Primitives;

namespace Helium.Algorithms;

/// <summary>
/// Finite topological space over an explicitly ordered finite point set.
/// </summary>
public readonly struct FiniteTopology<T> : IEquatable<FiniteTopology<T>>
    where T : notnull, ITotalOrder<T>
{
    public Finset<T> Points { get; }
    public Finset<Finset<T>> OpenSets { get; }

    public FiniteTopology(Finset<T> points, Finset<Finset<T>> openSets)
    {
        ValidateTopology(points, openSets);
        Points = points;
        OpenSets = openSets;
    }

    public static FiniteTopology<T> Discrete(Finset<T> points) =>
        new(points, Finset<Finset<T>>.FromElements(points.Powerset()));

    public static FiniteTopology<T> Indiscrete(Finset<T> points) =>
        new(points, Finset<Finset<T>>.Of(Finset<T>.Empty, points));

    public bool IsOpen(Finset<T> subset)
    {
        RequireSubsetOfPoints(subset);
        return OpenSets.Contains(subset);
    }

    public bool IsClosed(Finset<T> subset)
    {
        RequireSubsetOfPoints(subset);
        return IsOpen(Complement(subset));
    }

    public Finset<T> Interior(Finset<T> subset)
    {
        RequireSubsetOfPoints(subset);

        var result = Finset<T>.Empty;
        foreach (var open in OpenSets.Elements)
        {
            if (IsSubset(open, subset))
                result = result.Union(open);
        }

        return result;
    }

    public Finset<T> Closure(Finset<T> subset)
    {
        RequireSubsetOfPoints(subset);
        return Complement(Interior(Complement(subset)));
    }

    public Finset<T> Boundary(Finset<T> subset)
    {
        RequireSubsetOfPoints(subset);
        return Closure(subset).SDiff(Interior(subset));
    }

    public bool IsConnected()
    {
        foreach (var open in OpenSets.Elements)
        {
            if (open.IsEmpty || open == Points)
                continue;

            if (IsClosed(open))
                return false;
        }

        return true;
    }

    public static bool IsContinuous<TDomain, TCodomain>(
        FiniteTopology<TDomain> domain,
        FiniteTopology<TCodomain> codomain,
        Func<TDomain, TCodomain> map)
        where TDomain : notnull, ITotalOrder<TDomain>
        where TCodomain : notnull, ITotalOrder<TCodomain>
    {
        foreach (var point in domain.Points.Elements)
        {
            if (!codomain.Points.Contains(map(point)))
                throw new ArgumentException("The map sends a domain point outside the codomain.", nameof(map));
        }

        foreach (var codomainOpen in codomain.OpenSets.Elements)
        {
            var preimage = domain.Points.Filter(point => codomainOpen.Contains(map(point)));
            if (!domain.IsOpen(preimage))
                return false;
        }

        return true;
    }

    public bool Equals(FiniteTopology<T> other) =>
        Points == other.Points && OpenSets == other.OpenSets;

    public override bool Equals(object? obj) => obj is FiniteTopology<T> other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Points, OpenSets);
    public static bool operator ==(FiniteTopology<T> left, FiniteTopology<T> right) => left.Equals(right);
    public static bool operator !=(FiniteTopology<T> left, FiniteTopology<T> right) => !left.Equals(right);

    private Finset<T> Complement(Finset<T> subset) => Points.SDiff(subset);

    private void RequireSubsetOfPoints(Finset<T> subset)
    {
        if (!IsSubset(subset, Points))
            throw new ArgumentException("The subset contains a point outside the topology.");
    }

    private static void ValidateTopology(Finset<T> points, Finset<Finset<T>> openSets)
    {
        if (!openSets.Contains(Finset<T>.Empty))
            throw new ArgumentException("A topology must contain the empty set.", nameof(openSets));
        if (!openSets.Contains(points))
            throw new ArgumentException("A topology must contain the full point set.", nameof(openSets));

        foreach (var open in openSets.Elements)
        {
            if (!IsSubset(open, points))
                throw new ArgumentException("Every open set must be a subset of the point set.", nameof(openSets));
        }

        foreach (var left in openSets.Elements)
        foreach (var right in openSets.Elements)
        {
            if (!openSets.Contains(left.Union(right)))
                throw new ArgumentException("A finite topology must be closed under finite unions.", nameof(openSets));
            if (!openSets.Contains(left.Inter(right)))
                throw new ArgumentException("A finite topology must be closed under finite intersections.", nameof(openSets));
        }
    }

    private static bool IsSubset(Finset<T> candidate, Finset<T> container) =>
        candidate.SDiff(container).IsEmpty;
}
