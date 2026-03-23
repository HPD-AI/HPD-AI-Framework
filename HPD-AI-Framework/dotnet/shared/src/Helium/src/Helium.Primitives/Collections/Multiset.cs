using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Helium.Primitives;

/// <summary>
/// Unordered finite collection with multiplicity (a "bag").
/// Invariant: no entry has count zero or negative.
/// </summary>
[CollectionBuilder(typeof(Multiset), nameof(Multiset.Create))]
public readonly struct Multiset<T> : IEquatable<Multiset<T>>
    where T : notnull, IEquatable<T>, IComparable<T>
{
    private readonly ImmutableSortedDictionary<T, int>? _data;

    private ImmutableSortedDictionary<T, int> Data =>
        _data ?? ImmutableSortedDictionary<T, int>.Empty;

    private Multiset(ImmutableSortedDictionary<T, int> data)
    {
        _data = data.IsEmpty ? null : data;
    }

    // --- Construction ---

    public static Multiset<T> Empty => default;

    public static Multiset<T> FromElements(IEnumerable<T> elements)
    {
        var builder = ImmutableSortedDictionary.CreateBuilder<T, int>();
        foreach (var e in elements)
        {
            builder.TryGetValue(e, out var count);
            builder[e] = count + 1;
        }
        return new(builder.ToImmutable());
    }

    public static Multiset<T> FromCounts(IEnumerable<KeyValuePair<T, int>> counts)
    {
        var builder = ImmutableSortedDictionary.CreateBuilder<T, int>();
        foreach (var (element, count) in counts)
        {
            if (count > 0)
                builder[element] = count;
        }
        return new(builder.ToImmutable());
    }

    // --- Count and Card ---

    public int Count(T element) => Data.TryGetValue(element, out var c) ? c : 0;

    public int Card => Data.Values.Sum();

    public bool IsEmpty => _data is null || _data.IsEmpty;

    public IEnumerable<T> DistinctElements => Data.Keys;

    // --- Modification (returns new Multiset) ---

    public Multiset<T> Add(T element)
    {
        var count = Count(element);
        return new(Data.SetItem(element, count + 1));
    }

    public Multiset<T> Remove(T element)
    {
        var count = Count(element);
        if (count <= 1)
            return new(Data.Remove(element));
        return new(Data.SetItem(element, count - 1));
    }

    // --- Set-like operations ---

    /// <summary>Union: max of multiplicities for each element.</summary>
    public Multiset<T> Union(Multiset<T> other)
    {
        var builder = ImmutableSortedDictionary.CreateBuilder<T, int>();
        foreach (var (e, c) in Data)
            builder[e] = Math.Max(c, other.Count(e));
        foreach (var (e, c) in other.Data)
        {
            if (!builder.ContainsKey(e))
                builder[e] = c;
        }
        return new(builder.ToImmutable());
    }

    /// <summary>Intersection: min of multiplicities for each element.</summary>
    public Multiset<T> Inter(Multiset<T> other)
    {
        var builder = ImmutableSortedDictionary.CreateBuilder<T, int>();
        foreach (var (e, c) in Data)
        {
            var min = Math.Min(c, other.Count(e));
            if (min > 0)
                builder[e] = min;
        }
        return new(builder.ToImmutable());
    }

    /// <summary>Sum (disjoint union): sum of multiplicities.</summary>
    public Multiset<T> Sum(Multiset<T> other)
    {
        var builder = ImmutableSortedDictionary.CreateBuilder<T, int>();
        foreach (var (e, c) in Data)
            builder[e] = c;
        foreach (var (e, c) in other.Data)
        {
            builder.TryGetValue(e, out var existing);
            builder[e] = existing + c;
        }
        return new(builder.ToImmutable());
    }

    // --- Functional operations ---

    public Multiset<U> Map<U>(Func<T, U> f) where U : notnull, IEquatable<U>, IComparable<U>
    {
        var builder = ImmutableSortedDictionary.CreateBuilder<U, int>();
        foreach (var (e, c) in Data)
        {
            var mapped = f(e);
            builder.TryGetValue(mapped, out var existing);
            builder[mapped] = existing + c;
        }
        return Multiset<U>.FromCounts(builder);
    }

    public Multiset<T> Filter(Func<T, bool> predicate)
    {
        var builder = ImmutableSortedDictionary.CreateBuilder<T, int>();
        foreach (var (e, c) in Data)
        {
            if (predicate(e))
                builder[e] = c;
        }
        return new(builder.ToImmutable());
    }

    /// <summary>Drop multiplicities, keep distinct elements.</summary>
    public Finset<T> ToFinset() => Finset<T>.FromElements(Data.Keys);

    // --- Equality ---

    public bool Equals(Multiset<T> other)
    {
        if (Data.Count != other.Data.Count)
            return false;
        foreach (var (e, c) in Data)
        {
            if (!other.Data.TryGetValue(e, out var otherCount) || c != otherCount)
                return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is Multiset<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var (e, c) in Data)
        {
            hash.Add(e);
            hash.Add(c);
        }
        return hash.ToHashCode();
    }

    public static bool operator ==(Multiset<T> left, Multiset<T> right) => left.Equals(right);
    public static bool operator !=(Multiset<T> left, Multiset<T> right) => !left.Equals(right);

    public override string ToString()
    {
        if (IsEmpty) return "{}";
        var elements = Data.SelectMany(kv => Enumerable.Repeat(kv.Key.ToString()!, kv.Value));
        return "{" + string.Join(", ", elements) + "}";
    }
}

/// <summary>
/// Non-generic companion for CollectionBuilder support: Multiset&lt;T&gt; m = [1, 2, 2, 3];
/// </summary>
public static class Multiset
{
    public static Multiset<T> Create<T>(ReadOnlySpan<T> values)
        where T : notnull, IEquatable<T>, IComparable<T>
        => Multiset<T>.FromElements(values.ToArray());
}
