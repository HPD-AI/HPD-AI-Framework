using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Helium.Primitives;

/// <summary>
/// Concrete finite set with no duplicate elements.
/// Invariant: no duplicates, maintained by the sorted structure.
/// </summary>
[CollectionBuilder(typeof(Finset), nameof(Finset.Create))]
public readonly struct Finset<T> : IEquatable<Finset<T>>
    where T : notnull, IEquatable<T>, IComparable<T>
{
    private readonly ImmutableSortedSet<T>? _data;

    private ImmutableSortedSet<T> Data =>
        _data ?? ImmutableSortedSet<T>.Empty;

    private Finset(ImmutableSortedSet<T> data)
    {
        _data = data.IsEmpty ? null : data;
    }

    // --- Construction ---

    public static Finset<T> Empty => default;

    public static Finset<T> FromElements(IEnumerable<T> elements) =>
        new(elements.ToImmutableSortedSet());

    public static Finset<T> Of(params ReadOnlySpan<T> elements) =>
        FromElements(elements.ToArray());

    // --- Membership and Card ---

    public bool Contains(T element) => Data.Contains(element);
    public int Card => Data.Count;
    public bool IsEmpty => _data is null || _data.IsEmpty;

    public IEnumerable<T> Elements => Data;

    // --- Modification ---

    public Finset<T> Insert(T element) => new(Data.Add(element));
    public Finset<T> Erase(T element) => new(Data.Remove(element));

    // --- Set operations ---

    public Finset<T> Union(Finset<T> other) => new(Data.Union(other.Data));
    public Finset<T> Inter(Finset<T> other) => new(Data.Intersect(other.Data));
    public Finset<T> SDiff(Finset<T> other) => new(Data.Except(other.Data));

    // --- Functional operations ---

    public Finset<T> Filter(Func<T, bool> predicate) =>
        new(Data.Where(predicate).ToImmutableSortedSet());

    public Finset<U> Image<U>(Func<T, U> f) where U : notnull, IEquatable<U>, IComparable<U> =>
        new(Data.Select(f).ToImmutableSortedSet());

    // --- Aggregation ---

    public R Sum<R>(Func<T, R> f) where R : IRing<R>
    {
        var result = R.AdditiveIdentity;
        foreach (var e in Data)
            result = result + f(e);
        return result;
    }

    public R Prod<R>(Func<T, R> f) where R : IRing<R>
    {
        var result = R.MultiplicativeIdentity;
        foreach (var e in Data)
            result = result * f(e);
        return result;
    }

    // --- Combinatorial operations ---

    /// <summary>All subsets.</summary>
    public IEnumerable<Finset<T>> Powerset()
    {
        var elements = Data.ToArray();
        int n = elements.Length;
        for (int mask = 0; mask < (1 << n); mask++)
        {
            var builder = ImmutableSortedSet.CreateBuilder<T>();
            for (int i = 0; i < n; i++)
            {
                if ((mask & (1 << i)) != 0)
                    builder.Add(elements[i]);
            }
            yield return new Finset<T>(builder.ToImmutable());
        }
    }

    /// <summary>All subsets of exactly k elements.</summary>
    public IEnumerable<Finset<T>> PowersetCard(int k)
    {
        var elements = Data.ToArray();
        return Combinations(elements, k);
    }

    private static IEnumerable<Finset<T>> Combinations(T[] elements, int k)
    {
        if (k < 0 || k > elements.Length)
            yield break;
        if (k == 0)
        {
            yield return Empty;
            yield break;
        }

        var indices = new int[k];
        for (int i = 0; i < k; i++)
            indices[i] = i;

        while (true)
        {
            var builder = ImmutableSortedSet.CreateBuilder<T>();
            for (int i = 0; i < k; i++)
                builder.Add(elements[indices[i]]);
            yield return new Finset<T>(builder.ToImmutable());

            int pos = k - 1;
            while (pos >= 0 && indices[pos] == elements.Length - k + pos)
                pos--;
            if (pos < 0)
                yield break;
            indices[pos]++;
            for (int i = pos + 1; i < k; i++)
                indices[i] = indices[i - 1] + 1;
        }
    }

    // --- Equality ---

    public bool Equals(Finset<T> other) => Data.SetEquals(other.Data);

    public override bool Equals(object? obj) => obj is Finset<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var e in Data)
            hash.Add(e);
        return hash.ToHashCode();
    }

    public static bool operator ==(Finset<T> left, Finset<T> right) => left.Equals(right);
    public static bool operator !=(Finset<T> left, Finset<T> right) => !left.Equals(right);

    public override string ToString()
    {
        if (IsEmpty) return "{}";
        return "{" + string.Join(", ", Data) + "}";
    }
}

/// <summary>
/// Non-generic companion for CollectionBuilder support: Finset&lt;T&gt; s = [1, 2, 3];
/// </summary>
public static class Finset
{
    public static Finset<T> Create<T>(ReadOnlySpan<T> values)
        where T : notnull, IEquatable<T>, IComparable<T>
        => Finset<T>.Of(values);
}
