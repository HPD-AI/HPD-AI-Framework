using System.Numerics;
using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// Exact p-typical Witt vector truncated to static length N.
/// The initial implementation supports lengths 1 and 2 with polynomial Witt arithmetic.
/// </summary>
public readonly struct TruncatedWittVector<R, P, N> :
    ICommRing<TruncatedWittVector<R, P, N>>,
    IEquatable<TruncatedWittVector<R, P, N>>,
    IDecidableEq<TruncatedWittVector<R, P, N>>
    where R : ICommRing<R>
    where P : IPrimeModulus
    where N : IStaticPrecision
{
    private readonly R[]? _components;

    private R[] ComponentArray => _components ?? NewZeroArray();

    public static int Length
    {
        get
        {
            if (N.Value <= 0)
                throw new InvalidOperationException("Truncated Witt vector length must be positive.");
            if (N.Value > 2)
                throw new NotSupportedException("The initial Witt vector implementation supports lengths 1 and 2.");
            return N.Value;
        }
    }

    public static int Prime
    {
        get
        {
            var value = (BigInteger)P.Value;
            if (value <= 1 || value > int.MaxValue)
                throw new InvalidOperationException("The initial Witt vector implementation requires a small positive prime.");
            return (int)value;
        }
    }

    public TruncatedWittVector(ReadOnlySpan<R> components)
    {
        if (components.Length != Length)
            throw new ArgumentException("Component count must match the static Witt vector length.", nameof(components));

        _components = components.ToArray();
    }

    static TruncatedWittVector<R, P, N> IAdditiveIdentity<TruncatedWittVector<R, P, N>, TruncatedWittVector<R, P, N>>.AdditiveIdentity => Zero;
    static TruncatedWittVector<R, P, N> IMultiplicativeIdentity<TruncatedWittVector<R, P, N>, TruncatedWittVector<R, P, N>>.MultiplicativeIdentity => One;

    public static TruncatedWittVector<R, P, N> Zero => Constant(R.AdditiveIdentity);

    public static TruncatedWittVector<R, P, N> One
    {
        get
        {
            var values = NewZeroArray();
            values[0] = R.MultiplicativeIdentity;
            return new TruncatedWittVector<R, P, N>(values);
        }
    }

    public R this[int index]
    {
        get
        {
            if (index < 0 || index >= Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return ComponentArray[index];
        }
    }

    public IEnumerable<R> Components => ComponentArray;

    public R GhostComponent(int index)
    {
        if (index < 0 || index >= Length)
            throw new ArgumentOutOfRangeException(nameof(index));

        if (index == 0)
            return this[0];

        var p = Prime;
        return Pow(this[0], p) + R.FromInt(p) * this[1];
    }

    public static TruncatedWittVector<R, P, N> operator +(TruncatedWittVector<R, P, N> left, TruncatedWittVector<R, P, N> right)
    {
        var values = NewZeroArray();
        values[0] = left[0] + right[0];
        if (Length == 2)
        {
            var p = Prime;
            var correction = WittAdditionCorrection(left[0], right[0], p);
            values[1] = left[1] + right[1] - correction;
        }

        return new TruncatedWittVector<R, P, N>(values);
    }

    public static TruncatedWittVector<R, P, N> operator -(TruncatedWittVector<R, P, N> left, TruncatedWittVector<R, P, N> right) =>
        left + (-right);

    public static TruncatedWittVector<R, P, N> operator -(TruncatedWittVector<R, P, N> value)
    {
        var values = NewZeroArray();
        values[0] = -value[0];
        if (Length == 2)
        {
            var p = Prime;
            var correction = WittAdditionCorrection(value[0], values[0], p);
            values[1] = -value[1] + correction;
        }

        return new TruncatedWittVector<R, P, N>(values);
    }

    public static TruncatedWittVector<R, P, N> operator *(TruncatedWittVector<R, P, N> left, TruncatedWittVector<R, P, N> right)
    {
        var values = NewZeroArray();
        values[0] = left[0] * right[0];
        if (Length == 2)
        {
            var p = Prime;
            values[1] =
                Pow(left[0], p) * right[1] +
                Pow(right[0], p) * left[1] +
                R.FromInt(p) * left[1] * right[1];
        }

        return new TruncatedWittVector<R, P, N>(values);
    }

    public static TruncatedWittVector<R, P, N> FromInt(int n) =>
        Constant(R.FromInt(n));

    public static bool DecidableEquals(TruncatedWittVector<R, P, N> left, TruncatedWittVector<R, P, N> right) => left == right;

    public bool Equals(TruncatedWittVector<R, P, N> other)
    {
        for (int i = 0; i < Length; i++)
        {
            if (this[i] != other[i])
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is TruncatedWittVector<R, P, N> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var component in Components)
            hash.Add(component);
        return hash.ToHashCode();
    }

    public static bool operator ==(TruncatedWittVector<R, P, N> left, TruncatedWittVector<R, P, N> right) => left.Equals(right);
    public static bool operator !=(TruncatedWittVector<R, P, N> left, TruncatedWittVector<R, P, N> right) => !left.Equals(right);
    public override string ToString() => "(" + string.Join(", ", Components) + ")";

    public TruncatedWittVector<R, P, M> Truncate<M>() where M : IStaticPrecision
    {
        if (M.Value <= 0 || M.Value > Length)
            throw new ArgumentOutOfRangeException(nameof(M), "Target truncation length must be positive and no larger than source length.");

        var values = Components.Take(M.Value).ToArray();
        return new TruncatedWittVector<R, P, M>(values);
    }

    private static TruncatedWittVector<R, P, N> Constant(R value)
    {
        var values = NewZeroArray();
        values[0] = value;
        return new TruncatedWittVector<R, P, N>(values);
    }

    private static R[] NewZeroArray()
    {
        var values = new R[Length];
        Array.Fill(values, R.AdditiveIdentity);
        return values;
    }

    private static R Pow(R value, int exponent)
    {
        var result = R.MultiplicativeIdentity;
        for (int i = 0; i < exponent; i++)
            result *= value;
        return result;
    }

    private static R WittAdditionCorrection(R x, R y, int p)
    {
        var result = R.AdditiveIdentity;
        for (int i = 1; i < p; i++)
        {
            var coefficient = Binomial(p, i) / p;
            result += R.FromInt(coefficient) * Pow(x, i) * Pow(y, p - i);
        }

        return result;
    }

    private static int Binomial(int n, int k)
    {
        var result = 1;
        for (int i = 1; i <= k; i++)
            result = checked(result * (n - (k - i)) / i);
        return result;
    }
}
