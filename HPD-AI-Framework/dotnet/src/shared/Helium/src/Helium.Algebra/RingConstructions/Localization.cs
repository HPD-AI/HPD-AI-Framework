using System.Numerics;
using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// Localization of a commutative ring: fractions r/s where s is in a multiplicative subset.
/// For GCD domains, fractions are auto-reduced to canonical form.
///
/// The reduce function normalizes fractions (e.g., GCD reduction).
/// </summary>
public readonly struct Localization<R> :
    ICommRing<Localization<R>>,
    IEquatable<Localization<R>>
    where R : ICommRing<R>
{
    public R Numerator { get; }
    public R Denominator { get; }
    private readonly Func<R, R, (R Num, R Den)>? _normalize;

    internal Func<R, R, (R, R)>? Normalize => _normalize;

    private Localization(R numerator, R denominator, Func<R, R, (R Num, R Den)>? normalize)
    {
        if (normalize is not null)
        {
            (numerator, denominator) = normalize(numerator, denominator);
        }
        Numerator = numerator;
        Denominator = denominator;
        _normalize = normalize;
    }

    public static Localization<R> Create(R numerator, R denominator, Func<R, R, (R, R)>? normalize = null) =>
        new(numerator, denominator, normalize);

    // --- Identity elements ---

    public static Localization<R> AdditiveIdentity =>
        new(R.AdditiveIdentity, R.MultiplicativeIdentity, null);

    public static Localization<R> MultiplicativeIdentity =>
        new(R.MultiplicativeIdentity, R.MultiplicativeIdentity, null);

    static Localization<R> IAdditiveIdentity<Localization<R>, Localization<R>>.AdditiveIdentity => AdditiveIdentity;
    static Localization<R> IMultiplicativeIdentity<Localization<R>, Localization<R>>.MultiplicativeIdentity => MultiplicativeIdentity;

    // --- Arithmetic: a/b + c/d = (a*d + b*c) / (b*d) ---

    public static Localization<R> operator +(Localization<R> left, Localization<R> right)
    {
        var norm = left._normalize ?? right._normalize;
        return new(
            left.Numerator * right.Denominator + right.Numerator * left.Denominator,
            left.Denominator * right.Denominator,
            norm);
    }

    public static Localization<R> operator -(Localization<R> left, Localization<R> right)
    {
        var norm = left._normalize ?? right._normalize;
        return new(
            left.Numerator * right.Denominator - right.Numerator * left.Denominator,
            left.Denominator * right.Denominator,
            norm);
    }

    public static Localization<R> operator *(Localization<R> left, Localization<R> right)
    {
        var norm = left._normalize ?? right._normalize;
        return new(
            left.Numerator * right.Numerator,
            left.Denominator * right.Denominator,
            norm);
    }

    public static Localization<R> operator -(Localization<R> value)
    {
        return new(-value.Numerator, value.Denominator, value._normalize);
    }

    // --- Equality (cross-multiply: a/b == c/d iff a*d == c*b) ---

    public static bool operator ==(Localization<R> left, Localization<R> right) =>
        (left.Numerator * right.Denominator).Equals(right.Numerator * left.Denominator);

    public static bool operator !=(Localization<R> left, Localization<R> right) => !(left == right);

    public bool Equals(Localization<R> other) => this == other;
    public override bool Equals(object? obj) => obj is Localization<R> other && Equals(other);

    public override int GetHashCode()
    {
        // Not ideal for hashing since cross-multiply equality doesn't map to simple hash,
        // but works correctly for normalized fractions.
        return HashCode.Combine(Numerator, Denominator);
    }

    public override string ToString() =>
        Denominator.Equals(R.MultiplicativeIdentity) ? $"{Numerator}" : $"{Numerator}/{Denominator}";
}

