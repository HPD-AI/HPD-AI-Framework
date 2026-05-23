namespace Helium.Primitives;

/// <summary>
/// Context-owned finite powerset element. The universe is part of each value.
/// </summary>
public readonly struct FinitePowerSet<T> : IEquatable<FinitePowerSet<T>>
    where T : notnull, ITotalOrder<T>
{
    public Finset<T> Universe { get; }
    public Finset<T> Elements { get; }

    public FinitePowerSet(Finset<T> universe, Finset<T> elements)
    {
        var outside = elements.SDiff(universe);
        if (!outside.IsEmpty)
            throw new ArgumentException("Elements must be a subset of the universe.", nameof(elements));

        Universe = universe;
        Elements = elements;
    }

    public static FinitePowerSet<T> Bottom(Finset<T> universe) =>
        new(universe, Finset<T>.Empty);

    public static FinitePowerSet<T> Top(Finset<T> universe) =>
        new(universe, universe);

    public bool LessEqual(FinitePowerSet<T> other)
    {
        RequireSameUniverse(other);
        return Elements.SDiff(other.Elements).IsEmpty;
    }

    public FinitePowerSet<T> Join(FinitePowerSet<T> other)
    {
        RequireSameUniverse(other);
        return new(Universe, Elements.Union(other.Elements));
    }

    public FinitePowerSet<T> Meet(FinitePowerSet<T> other)
    {
        RequireSameUniverse(other);
        return new(Universe, Elements.Inter(other.Elements));
    }

    public FinitePowerSet<T> Complement() =>
        new(Universe, Universe.SDiff(Elements));

    public bool Equals(FinitePowerSet<T> other) =>
        Universe == other.Universe && Elements == other.Elements;

    public override bool Equals(object? obj) => obj is FinitePowerSet<T> other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Universe, Elements);
    public static bool operator ==(FinitePowerSet<T> left, FinitePowerSet<T> right) => left.Equals(right);
    public static bool operator !=(FinitePowerSet<T> left, FinitePowerSet<T> right) => !left.Equals(right);

    private void RequireSameUniverse(FinitePowerSet<T> other)
    {
        if (Universe != other.Universe)
            throw new InvalidOperationException("Finite powerset operations require the same universe.");
    }
}
