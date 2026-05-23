using System.Numerics;
using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// Exact finite-support series over an ordered additive commutative monoid of exponents.
/// </summary>
public readonly struct FiniteSupportSeries<G, R> :
    ICommRing<FiniteSupportSeries<G, R>>,
    IEquatable<FiniteSupportSeries<G, R>>,
    IFormattable
    where G : notnull, ITotalOrder<G>, IAddCommMonoid<G>
    where R : ICommRing<R>
{
    private readonly Finsupp<G, R> _coefficients;

    private FiniteSupportSeries(Finsupp<G, R> coefficients)
    {
        _coefficients = coefficients;
    }

    static FiniteSupportSeries<G, R> IAdditiveIdentity<FiniteSupportSeries<G, R>, FiniteSupportSeries<G, R>>.AdditiveIdentity => Zero;
    static FiniteSupportSeries<G, R> IMultiplicativeIdentity<FiniteSupportSeries<G, R>, FiniteSupportSeries<G, R>>.MultiplicativeIdentity => One;

    public static FiniteSupportSeries<G, R> Zero => new(Finsupp<G, R>.Empty);

    public static FiniteSupportSeries<G, R> One => Monomial(G.AdditiveIdentity, R.MultiplicativeIdentity);

    public bool IsZero => _coefficients.IsZero;

    public IEnumerable<(G Exponent, R Coefficient)> Components
    {
        get
        {
            foreach (var exponent in _coefficients.Support)
                yield return (exponent, _coefficients[exponent]);
        }
    }

    public static FiniteSupportSeries<G, R> Monomial(G exponent, R coefficient) =>
        new(Finsupp<G, R>.Single(exponent, coefficient));

    public static FiniteSupportSeries<G, R> FromTerms(IEnumerable<(G Exponent, R Coefficient)> terms)
    {
        var scratch = new Dictionary<G, R>();
        foreach (var (exponent, coefficient) in terms)
        {
            if (scratch.TryGetValue(exponent, out var current))
                scratch[exponent] = current + coefficient;
            else
                scratch[exponent] = coefficient;
        }

        return new(Finsupp<G, R>.FromDictionary(scratch.Select(kv => new KeyValuePair<G, R>(kv.Key, kv.Value))));
    }

    public R Coefficient(G exponent) => _coefficients[exponent];

    public static FiniteSupportSeries<G, R> operator +(FiniteSupportSeries<G, R> left, FiniteSupportSeries<G, R> right) =>
        new(left._coefficients + right._coefficients);

    public static FiniteSupportSeries<G, R> operator -(FiniteSupportSeries<G, R> left, FiniteSupportSeries<G, R> right) =>
        new(left._coefficients - right._coefficients);

    public static FiniteSupportSeries<G, R> operator -(FiniteSupportSeries<G, R> value) =>
        new(-value._coefficients);

    public static FiniteSupportSeries<G, R> operator *(FiniteSupportSeries<G, R> left, FiniteSupportSeries<G, R> right)
    {
        if (left.IsZero || right.IsZero)
            return Zero;

        var scratch = new Dictionary<G, R>();
        foreach (var (leftExponent, leftCoefficient) in left.Components)
        foreach (var (rightExponent, rightCoefficient) in right.Components)
        {
            var exponent = leftExponent + rightExponent;
            var coefficient = leftCoefficient * rightCoefficient;
            scratch[exponent] = scratch.TryGetValue(exponent, out var current)
                ? current + coefficient
                : coefficient;
        }

        return new(Finsupp<G, R>.FromDictionary(scratch.Select(kv => new KeyValuePair<G, R>(kv.Key, kv.Value))));
    }

    public static FiniteSupportSeries<G, R> FromInt(int n) =>
        Monomial(G.AdditiveIdentity, R.FromInt(n));

    public bool Equals(FiniteSupportSeries<G, R> other) => _coefficients == other._coefficients;
    public override bool Equals(object? obj) => obj is FiniteSupportSeries<G, R> other && Equals(other);
    public override int GetHashCode() => _coefficients.GetHashCode();
    public static bool operator ==(FiniteSupportSeries<G, R> left, FiniteSupportSeries<G, R> right) => left.Equals(right);
    public static bool operator !=(FiniteSupportSeries<G, R> left, FiniteSupportSeries<G, R> right) => !left.Equals(right);

    public override string ToString() => ToString(null, null);

    public string ToString(string? format, IFormatProvider? provider)
    {
        if (IsZero)
            return "0";

        return string.Join(" + ", Components.Select(c =>
            $"{FormatHelpers.FormatElement(c.Coefficient, format, provider)}*t^{FormatHelpers.FormatElement(c.Exponent, format, provider)}"));
    }
}

/// <summary>
/// Exact finite-support series truncated by an explicit exponent predicate.
/// </summary>
public readonly struct TruncatedSeries<G, R> : IEquatable<TruncatedSeries<G, R>>, IFormattable
    where G : notnull, ITotalOrder<G>, IAddCommMonoid<G>
    where R : ICommRing<R>
{
    public FiniteSupportSeries<G, R> Value { get; }
    public G BoundExclusive { get; }

    public TruncatedSeries(FiniteSupportSeries<G, R> value, G boundExclusive)
    {
        Value = Truncate(value, boundExclusive);
        BoundExclusive = boundExclusive;
    }

    public R Coefficient(G exponent) => IsKept(exponent, BoundExclusive) ? Value.Coefficient(exponent) : R.AdditiveIdentity;

    public TruncatedSeries<G, R> Add(TruncatedSeries<G, R> other) =>
        SameBound(other, Value + other.Value);

    public TruncatedSeries<G, R> Multiply(TruncatedSeries<G, R> other) =>
        SameBound(other, Value * other.Value);

    public bool Equals(TruncatedSeries<G, R> other) => BoundExclusive.Equals(other.BoundExclusive) && Value == other.Value;
    public override bool Equals(object? obj) => obj is TruncatedSeries<G, R> other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Value, BoundExclusive);
    public static bool operator ==(TruncatedSeries<G, R> left, TruncatedSeries<G, R> right) => left.Equals(right);
    public static bool operator !=(TruncatedSeries<G, R> left, TruncatedSeries<G, R> right) => !left.Equals(right);
    public override string ToString() => Value.ToString();
    public string ToString(string? format, IFormatProvider? provider) => Value.ToString(format, provider);

    private TruncatedSeries<G, R> SameBound(TruncatedSeries<G, R> other, FiniteSupportSeries<G, R> value)
    {
        if (!BoundExclusive.Equals(other.BoundExclusive))
            throw new InvalidOperationException("Cannot combine truncated series with different bounds.");
        return new(value, BoundExclusive);
    }

    private static FiniteSupportSeries<G, R> Truncate(FiniteSupportSeries<G, R> value, G boundExclusive) =>
        FiniteSupportSeries<G, R>.FromTerms(value.Components.Where(c => IsKept(c.Exponent, boundExclusive)));

    private static bool IsKept(G exponent, G boundExclusive) =>
        G.CompareOrder(exponent, boundExclusive) == Ordering.Less;
}
