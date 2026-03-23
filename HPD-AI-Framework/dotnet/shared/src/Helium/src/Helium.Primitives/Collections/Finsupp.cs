using System.Collections.Immutable;
using System.Numerics;

namespace Helium.Primitives;

/// <summary>
/// Finitely supported function: a sparse, immutable map from keys to values where missing
/// keys are implicitly zero. The critical invariant: no entry has value equal to the
/// additive identity. Setting a key to zero removes it.
/// </summary>
public readonly struct Finsupp<TKey, TValue> : IEquatable<Finsupp<TKey, TValue>>
    where TKey : notnull, IComparable<TKey>, IEquatable<TKey>
    where TValue : IAdditiveIdentity<TValue, TValue>, IEqualityOperators<TValue, TValue, bool>
{
    private readonly ImmutableSortedDictionary<TKey, TValue>? _data;

    private ImmutableSortedDictionary<TKey, TValue> Data =>
        _data ?? ImmutableSortedDictionary<TKey, TValue>.Empty;

    private Finsupp(ImmutableSortedDictionary<TKey, TValue> data)
    {
        _data = data.IsEmpty ? null : data;
    }

    // --- Construction ---

    public static Finsupp<TKey, TValue> Empty => default;

    public static Finsupp<TKey, TValue> Single(TKey key, TValue value)
    {
        if (value == TValue.AdditiveIdentity)
            return Empty;
        return new(ImmutableSortedDictionary<TKey, TValue>.Empty.Add(key, value));
    }

    public static Finsupp<TKey, TValue> FromDictionary(IEnumerable<KeyValuePair<TKey, TValue>> pairs)
    {
        var builder = ImmutableSortedDictionary.CreateBuilder<TKey, TValue>();
        foreach (var (key, value) in pairs)
        {
            if (value != TValue.AdditiveIdentity)
                builder[key] = value;
        }
        return new(builder.ToImmutable());
    }

    // --- Lookup ---

    public TValue this[TKey key] =>
        Data.TryGetValue(key, out var value) ? value : TValue.AdditiveIdentity;

    // --- Support ---

    public IEnumerable<TKey> Support => Data.Keys;
    public int Count => Data.Count;
    public bool IsZero => _data is null || _data.IsEmpty;

    // --- Core operations ---

    /// <summary>
    /// Pointwise binary operation. Result strips zeros.
    /// f must satisfy f(0, 0) == 0 for correct semantics.
    /// </summary>
    public static Finsupp<TKey, TOut> ZipWith<TOut>(
        Func<TValue, TValue, TOut> f,
        Finsupp<TKey, TValue> a,
        Finsupp<TKey, TValue> b)
        where TOut : IAdditiveIdentity<TOut, TOut>, IEqualityOperators<TOut, TOut, bool>
    {
        var builder = ImmutableSortedDictionary.CreateBuilder<TKey, TOut>();
        var zero = TValue.AdditiveIdentity;

        // All keys from a.
        foreach (var (key, va) in a.Data)
        {
            var vb = b.Data.TryGetValue(key, out var bVal) ? bVal : zero;
            var result = f(va, vb);
            if (result != TOut.AdditiveIdentity)
                builder[key] = result;
        }

        // Keys only in b.
        foreach (var (key, vb) in b.Data)
        {
            if (!a.Data.ContainsKey(key))
            {
                var result = f(zero, vb);
                if (result != TOut.AdditiveIdentity)
                    builder[key] = result;
            }
        }

        return new(builder.ToImmutable());
    }

    /// <summary>
    /// Transform all values. Result strips zeros.
    /// f must satisfy f(0) == 0 for correct semantics.
    /// </summary>
    public static Finsupp<TKey, TOut> MapRange<TOut>(
        Func<TValue, TOut> f,
        Finsupp<TKey, TValue> a)
        where TOut : IAdditiveIdentity<TOut, TOut>, IEqualityOperators<TOut, TOut, bool>
    {
        var builder = ImmutableSortedDictionary.CreateBuilder<TKey, TOut>();
        foreach (var (key, value) in a.Data)
        {
            var result = f(value);
            if (result != TOut.AdditiveIdentity)
                builder[key] = result;
        }
        return new(builder.ToImmutable());
    }

    /// <summary>
    /// Remap keys, summing collisions.
    /// </summary>
    public static Finsupp<TNewKey, TValue> MapKeys<TNewKey>(
        Func<TKey, TNewKey> f,
        Finsupp<TKey, TValue> a,
        Func<TValue, TValue, TValue> add)
        where TNewKey : notnull, IComparable<TNewKey>, IEquatable<TNewKey>
    {
        var builder = ImmutableSortedDictionary.CreateBuilder<TNewKey, TValue>();
        foreach (var (key, value) in a.Data)
        {
            var newKey = f(key);
            if (builder.TryGetValue(newKey, out var existing))
            {
                var sum = add(existing, value);
                if (sum == TValue.AdditiveIdentity)
                    builder.Remove(newKey);
                else
                    builder[newKey] = sum;
            }
            else
            {
                if (value != TValue.AdditiveIdentity)
                    builder[newKey] = value;
            }
        }
        return new(builder.ToImmutable());
    }

    /// <summary>
    /// Fold over support: sum_{k in support} f(k, this[k]).
    /// </summary>
    public TResult Sum<TResult>(Func<TKey, TValue, TResult> f, TResult seed, Func<TResult, TResult, TResult> add)
    {
        var result = seed;
        foreach (var (key, value) in Data)
            result = add(result, f(key, value));
        return result;
    }

    /// <summary>
    /// Set a key to a value. Returns a new Finsupp. Removes key if value is zero.
    /// </summary>
    public Finsupp<TKey, TValue> Set(TKey key, TValue value)
    {
        if (value == TValue.AdditiveIdentity)
            return new(Data.Remove(key));
        return new(Data.SetItem(key, value));
    }

    // --- Equality ---

    public bool Equals(Finsupp<TKey, TValue> other)
    {
        if (Count != other.Count)
            return false;
        foreach (var (key, value) in Data)
        {
            if (!other.Data.TryGetValue(key, out var otherValue) || value != otherValue)
                return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is Finsupp<TKey, TValue> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var (key, value) in Data)
        {
            hash.Add(key);
            hash.Add(value);
        }
        return hash.ToHashCode();
    }

    public static bool operator ==(Finsupp<TKey, TValue> left, Finsupp<TKey, TValue> right) => left.Equals(right);
    public static bool operator !=(Finsupp<TKey, TValue> left, Finsupp<TKey, TValue> right) => !left.Equals(right);

    public override string ToString()
    {
        if (IsZero) return "0";
        return string.Join(" + ", Data.Select(kv => $"({kv.Key}: {kv.Value})"));
    }
}

/// <summary>
/// Ring operations on Finsupp when TValue has ring structure.
/// C# 14 extension block: adds operators conditionally based on type parameter constraints.
/// </summary>
public static class FinsuppRingExtensions
{
    extension<TKey, TValue>(Finsupp<TKey, TValue> self)
        where TKey : notnull, IComparable<TKey>, IEquatable<TKey>
        where TValue : IRing<TValue>, IAdditiveIdentity<TValue, TValue>,
                       IEqualityOperators<TValue, TValue, bool>
    {
        public static Finsupp<TKey, TValue> operator +(
            Finsupp<TKey, TValue> a, Finsupp<TKey, TValue> b)
            => Finsupp<TKey, TValue>.ZipWith((x, y) => x + y, a, b);

        public static Finsupp<TKey, TValue> operator -(
            Finsupp<TKey, TValue> a, Finsupp<TKey, TValue> b)
            => Finsupp<TKey, TValue>.ZipWith((x, y) => x - y, a, b);

        public static Finsupp<TKey, TValue> operator -(Finsupp<TKey, TValue> a)
            => Finsupp<TKey, TValue>.MapRange(x => -x, a);

        public Finsupp<TKey, TValue> ScalarMultiply(TValue scalar)
        {
            if (scalar == TValue.AdditiveIdentity)
                return Finsupp<TKey, TValue>.Empty;
            return Finsupp<TKey, TValue>.MapRange(x => scalar * x, self);
        }
    }
}
