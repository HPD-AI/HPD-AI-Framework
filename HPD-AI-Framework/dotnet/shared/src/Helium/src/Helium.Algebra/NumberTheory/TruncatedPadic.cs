using System.Numerics;
using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// Exact p-adic residue modulo p^N, with p and N carried by static witnesses.
/// </summary>
public readonly struct TruncatedPadic<P, N> :
    ICommRing<TruncatedPadic<P, N>>,
    IEquatable<TruncatedPadic<P, N>>,
    IDecidableEq<TruncatedPadic<P, N>>,
    IFormattable
    where P : IPrimeModulus
    where N : IStaticPrecision
{
    public Integer Value { get; }

    public static Integer Prime => P.Value;

    public static int Precision
    {
        get
        {
            if (N.Value <= 0)
                throw new InvalidOperationException("Truncated p-adic precision must be positive.");
            return N.Value;
        }
    }

    public static Integer Modulus => Integer.Pow(Prime, Precision);

    public TruncatedPadic(Integer value)
    {
        Value = Reduce(value);
    }

    static TruncatedPadic<P, N> IAdditiveIdentity<TruncatedPadic<P, N>, TruncatedPadic<P, N>>.AdditiveIdentity => Zero;
    static TruncatedPadic<P, N> IMultiplicativeIdentity<TruncatedPadic<P, N>, TruncatedPadic<P, N>>.MultiplicativeIdentity => One;

    public static TruncatedPadic<P, N> Zero => new(Integer.Zero);
    public static TruncatedPadic<P, N> One => new(Integer.One);
    public bool IsZero => Value.IsZero;
    public bool IsUnit => !IsDivisibleByPrime(Value);

    public int Valuation()
    {
        if (Value.IsZero)
            return Precision;

        var count = 0;
        var value = Value;
        while (count < Precision)
        {
            var (_, remainder) = Integer.DivMod(value, Prime);
            if (!remainder.IsZero)
                break;
            var (quotient, _) = Integer.DivMod(value, Prime);
            value = quotient;
            count++;
        }

        return count;
    }

    public static TruncatedPadic<P, N> operator +(TruncatedPadic<P, N> left, TruncatedPadic<P, N> right) =>
        new(left.Value + right.Value);

    public static TruncatedPadic<P, N> operator -(TruncatedPadic<P, N> left, TruncatedPadic<P, N> right) =>
        new(left.Value - right.Value);

    public static TruncatedPadic<P, N> operator -(TruncatedPadic<P, N> value) =>
        new(-value.Value);

    public static TruncatedPadic<P, N> operator *(TruncatedPadic<P, N> left, TruncatedPadic<P, N> right) =>
        new(left.Value * right.Value);

    public static TruncatedPadic<P, N> FromInt(int n) => new((Integer)n);

    public static bool TryInvert(TruncatedPadic<P, N> value, out TruncatedPadic<P, N> inverse)
    {
        var maybeInverse = IntegerNumberTheory.ModInverse(value.Value, Modulus);
        if (maybeInverse is null)
        {
            inverse = Zero;
            return false;
        }

        inverse = new TruncatedPadic<P, N>(maybeInverse.Value);
        return true;
    }

    public static bool DecidableEquals(TruncatedPadic<P, N> left, TruncatedPadic<P, N> right) => left == right;
    public bool Equals(TruncatedPadic<P, N> other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is TruncatedPadic<P, N> other && Equals(other);
    public override int GetHashCode() => Value.GetHashCode();
    public static bool operator ==(TruncatedPadic<P, N> left, TruncatedPadic<P, N> right) => left.Equals(right);
    public static bool operator !=(TruncatedPadic<P, N> left, TruncatedPadic<P, N> right) => !left.Equals(right);
    public override string ToString() => $"{Value} (mod {Prime}^{Precision})";
    public string ToString(string? format, IFormatProvider? provider) =>
        $"{Value.ToString(format, provider)} (mod {Prime.ToString(format, provider)}^{Precision})";

    private static Integer Reduce(Integer value)
    {
        var modulus = Modulus;
        var (_, remainder) = Integer.DivMod(value, modulus);
        return remainder.Sign < 0 ? remainder + modulus : remainder;
    }

    private static bool IsDivisibleByPrime(Integer value)
    {
        var (_, remainder) = Integer.DivMod(value, Prime);
        return remainder.IsZero;
    }
}
