using System.Collections;

namespace Helium.Primitives;

/// <summary>
/// Immutable finite list. Equality is pointwise decidable equality.
/// </summary>
public readonly struct FiniteList<T> :
    IEnumerable<T>,
    IEquatable<FiniteList<T>>,
    IDecidableEq<FiniteList<T>>
    where T : IDecidableEq<T>
{
    private readonly T[]? _items;

    private T[] Items => _items ?? [];

    private FiniteList(T[] items)
    {
        _items = items.Length == 0 ? null : items;
    }

    public static FiniteList<T> Empty => default;

    public static FiniteList<T> FromEnumerable(IEnumerable<T> values) =>
        new(values.ToArray());

    public static FiniteList<T> Of(params ReadOnlySpan<T> values) =>
        new(values.ToArray());

    public int Length => Items.Length;
    public bool IsEmpty => Length == 0;

    public T this[int index] => Items[index];

    public FiniteList<T> Cons(T head)
    {
        var result = new T[Length + 1];
        result[0] = head;
        Array.Copy(Items, 0, result, 1, Length);
        return new(result);
    }

    public FiniteList<T> Append(FiniteList<T> other)
    {
        if (IsEmpty) return other;
        if (other.IsEmpty) return this;

        var result = new T[Length + other.Length];
        Array.Copy(Items, result, Length);
        Array.Copy(other.Items, 0, result, Length, other.Length);
        return new(result);
    }

    public FiniteList<T> Reverse()
    {
        var result = Items.ToArray();
        Array.Reverse(result);
        return new(result);
    }

    public T Head
    {
        get
        {
            if (IsEmpty)
                throw new InvalidOperationException("Empty list has no head.");
            return Items[0];
        }
    }

    public FiniteList<T> Tail
    {
        get
        {
            if (IsEmpty)
                throw new InvalidOperationException("Empty list has no tail.");
            return new(Items[1..]);
        }
    }

    public FiniteList<U> Map<U>(Func<T, U> f)
        where U : IDecidableEq<U> =>
        new(Items.Select(f).ToArray());

    public FiniteList<T> Filter(Func<T, bool> predicate) =>
        new(Items.Where(predicate).ToArray());

    public U FoldLeft<U>(U seed, Func<U, T, U> f)
    {
        var result = seed;
        foreach (var item in Items)
            result = f(result, item);
        return result;
    }

    public U FoldRight<U>(U seed, Func<T, U, U> f)
    {
        var result = seed;
        for (int i = Length - 1; i >= 0; i--)
            result = f(Items[i], result);
        return result;
    }

    public FiniteList<Pair<T, U>> Zip<U>(FiniteList<U> other)
        where U : IDecidableEq<U>
    {
        var count = Math.Min(Length, other.Length);
        var result = new Pair<T, U>[count];
        for (int i = 0; i < count; i++)
            result[i] = new Pair<T, U>(Items[i], other[i]);
        return FiniteList<Pair<T, U>>.FromEnumerable(result);
    }

    public static FiniteList<T> Concat(IEnumerable<FiniteList<T>> lists)
    {
        var result = Empty;
        foreach (var list in lists)
            result = result.Append(list);
        return result;
    }

    public bool Contains(T value) =>
        Items.Any(item => T.DecidableEquals(item, value));

    public int IndexOf(T value)
    {
        for (int i = 0; i < Length; i++)
            if (T.DecidableEquals(Items[i], value))
                return i;
        return -1;
    }

    public bool All(Func<T, bool> predicate) => Items.All(predicate);
    public bool Any(Func<T, bool> predicate) => Items.Any(predicate);

    public FiniteList<T> Unique()
    {
        var result = new List<T>();
        foreach (var item in Items)
        {
            if (!result.Any(existing => T.DecidableEquals(existing, item)))
                result.Add(item);
        }
        return new(result.ToArray());
    }

    public bool Equals(FiniteList<T> other)
    {
        if (Length != other.Length)
            return false;

        for (int i = 0; i < Length; i++)
            if (!T.DecidableEquals(Items[i], other.Items[i]))
                return false;

        return true;
    }

    public override bool Equals(object? obj) => obj is FiniteList<T> other && Equals(other);
    public static bool DecidableEquals(FiniteList<T> left, FiniteList<T> right) => left.Equals(right);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in Items)
            hash.Add(item);
        return hash.ToHashCode();
    }

    public static bool operator ==(FiniteList<T> left, FiniteList<T> right) => left.Equals(right);
    public static bool operator !=(FiniteList<T> left, FiniteList<T> right) => !left.Equals(right);

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => "[" + string.Join(", ", Items) + "]";
}

public readonly record struct Pair<TLeft, TRight>(TLeft Left, TRight Right) :
    IDecidableEq<Pair<TLeft, TRight>>
    where TLeft : IDecidableEq<TLeft>
    where TRight : IDecidableEq<TRight>
{
    public static bool DecidableEquals(Pair<TLeft, TRight> left, Pair<TLeft, TRight> right) =>
        TLeft.DecidableEquals(left.Left, right.Left) &&
        TRight.DecidableEquals(left.Right, right.Right);
}

public static class FiniteListOrderExtensions
{
    extension<T>(FiniteList<T> self)
        where T : IDecidableEq<T>, ITotalOrder<T>
    {
        public FiniteList<T> Sort()
        {
            var items = self.ToArray();
            Array.Sort(items, TotalOrderComparer<T>.Instance);
            return FiniteList<T>.FromEnumerable(items);
        }
    }
}
