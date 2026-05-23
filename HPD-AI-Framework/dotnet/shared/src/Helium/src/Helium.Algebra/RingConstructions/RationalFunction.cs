using System.Numerics;
using System.Text;
using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// Rational function: ratio of two univariate polynomials p(x)/q(x).
/// Owns numerator/denominator arithmetic directly; equality is cross-multiply
/// equality in the polynomial ring.
/// </summary>
public readonly struct RationalFunction<R> :
    ICommRing<RationalFunction<R>>,
    IEquatable<RationalFunction<R>>,
    IFormattable
    where R : IRing<R>
{
    private readonly Func<SparsePolynomial<R>, SparsePolynomial<R>, (SparsePolynomial<R>, SparsePolynomial<R>)>? _normalize;

    private RationalFunction(
        SparsePolynomial<R> numerator,
        SparsePolynomial<R> denominator,
        Func<SparsePolynomial<R>, SparsePolynomial<R>, (SparsePolynomial<R>, SparsePolynomial<R>)>? normalize)
    {
        (Numerator, Denominator) = normalize is null
            ? (numerator, denominator)
            : normalize(numerator, denominator);
        _normalize = normalize;
    }

    public SparsePolynomial<R> Numerator { get; }
    public SparsePolynomial<R> Denominator { get; }

    // Expose the normalizer so extension methods can forward it.
    internal Func<SparsePolynomial<R>, SparsePolynomial<R>, (SparsePolynomial<R>, SparsePolynomial<R>)>? Normalize =>
        _normalize;

    // --- Construction ---

    public static RationalFunction<R> Create(SparsePolynomial<R> numerator, SparsePolynomial<R> denominator,
        Func<SparsePolynomial<R>, SparsePolynomial<R>, (SparsePolynomial<R>, SparsePolynomial<R>)>? normalize = null) =>
        new(numerator, denominator, normalize);

    public static RationalFunction<R> FromSparsePolynomial(SparsePolynomial<R> p) =>
        Create(p, SparsePolynomial<R>.One);

    public static RationalFunction<R> Zero => Create(SparsePolynomial<R>.Zero, SparsePolynomial<R>.One);
    public static RationalFunction<R> One  => Create(SparsePolynomial<R>.One,  SparsePolynomial<R>.One);

    public bool IsZero => Numerator.IsZero;

    // --- Identity elements ---

    static RationalFunction<R> IAdditiveIdentity<RationalFunction<R>, RationalFunction<R>>.AdditiveIdentity       => Zero;
    static RationalFunction<R> IMultiplicativeIdentity<RationalFunction<R>, RationalFunction<R>>.MultiplicativeIdentity => One;

    // --- Arithmetic ---

    public static RationalFunction<R> operator +(RationalFunction<R> left, RationalFunction<R> right) =>
        Create(
            left.Numerator * right.Denominator + right.Numerator * left.Denominator,
            left.Denominator * right.Denominator,
            left.Normalize ?? right.Normalize);

    public static RationalFunction<R> operator -(RationalFunction<R> left, RationalFunction<R> right) =>
        Create(
            left.Numerator * right.Denominator - right.Numerator * left.Denominator,
            left.Denominator * right.Denominator,
            left.Normalize ?? right.Normalize);

    public static RationalFunction<R> operator *(RationalFunction<R> left, RationalFunction<R> right) =>
        Create(
            left.Numerator * right.Numerator,
            left.Denominator * right.Denominator,
            left.Normalize ?? right.Normalize);

    public static RationalFunction<R> operator -(RationalFunction<R> value) =>
        Create(-value.Numerator, value.Denominator, value.Normalize);

    // --- Equality ---

    public static bool operator ==(RationalFunction<R> left, RationalFunction<R> right) =>
        (left.Numerator * right.Denominator).Equals(right.Numerator * left.Denominator);

    public static bool operator !=(RationalFunction<R> left, RationalFunction<R> right) => !(left == right);

    public bool Equals(RationalFunction<R> other) => this == other;
    public override bool Equals(object? obj) => obj is RationalFunction<R> other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);

    // --- Formatting ---

    public override string ToString() => ToString(null, null);

    public string ToString(string? format, IFormatProvider? provider)
    {
        if (IsZero) return "0";

        if (Denominator.Equals(SparsePolynomial<R>.One))
            return Numerator.ToString(format, provider);

        if (format == "M")
            return FormatMathML(provider);

        if (format == "L")
            return FormatLatex(provider);

        var numStr = Numerator.ToString(format, provider);
        var denStr = Denominator.ToString(format, provider);

        if (FormatHelpers.NeedsParentheses(numStr))
            numStr = $"({numStr})";
        if (FormatHelpers.NeedsParentheses(denStr))
            denStr = $"({denStr})";

        return $"{numStr}/{denStr}";
    }

    private string FormatLatex(IFormatProvider? provider)
    {
        var numStr = Numerator.ToString("L", provider);
        var denStr = Denominator.ToString("L", provider);
        return $"\\frac{{{numStr}}}{{{denStr}}}";
    }

    private string FormatMathML(IFormatProvider? provider)
    {
        var numStr = Numerator.ToString("M", provider);
        var denStr = Denominator.ToString("M", provider);
        return $"<mfrac>{numStr}{denStr}</mfrac>";
    }
}

/// <summary>
/// Field operations for RationalFunction when the coefficient ring is a field.
/// Adds GCD reduction and division.
/// </summary>
public static class RationalFunctionFieldExtensions
{
    extension<R>(RationalFunction<R> self) where R : IField<R>
    {
        /// <summary>
        /// Reduce to lowest terms via polynomial GCD.
        /// </summary>
        public RationalFunction<R> Reduce()
        {
            if (self.IsZero) return self;

            var gcd = self.Numerator.Gcd(self.Denominator);
            if (gcd.IsZero || gcd.Equals(SparsePolynomial<R>.One))
                return self;

            var (qN, _) = self.Numerator.DivMod(gcd);
            var (qD, _) = self.Denominator.DivMod(gcd);
            return RationalFunction<R>.Create(qN, qD, self.Normalize);
        }

        /// <summary>
        /// Division: (a/b) / (c/d) = (a*d) / (b*c).
        /// </summary>
        public RationalFunction<R> Divide(RationalFunction<R> other)
        {
            if (other.IsZero)
                return RationalFunction<R>.Zero;

            var norm = self.Normalize ?? other.Normalize;
            return RationalFunction<R>.Create(
                self.Numerator * other.Denominator,
                self.Denominator * other.Numerator,
                norm);
        }
    }
}

/// <summary>
/// Factory for constructing GCD-normalized rational functions over a field.
/// </summary>
public static class RationalFunctionField
{
    /// <summary>
    /// Create a rational function over a field with automatic GCD reduction.
    /// </summary>
    public static RationalFunction<R> Of<R>(SparsePolynomial<R> numerator, SparsePolynomial<R> denominator)
        where R : IField<R>
    {
        return RationalFunction<R>.Create(numerator, denominator, (n, d) =>
        {
            if (d.IsZero) return (SparsePolynomial<R>.Zero, SparsePolynomial<R>.One);
            if (n.IsZero) return (SparsePolynomial<R>.Zero, SparsePolynomial<R>.One);

            var gcd = n.Gcd(d);
            if (gcd.IsZero || gcd.Equals(SparsePolynomial<R>.One))
                return (n, d);

            var (qN, _) = n.DivMod(gcd);
            var (qD, _) = d.DivMod(gcd);
            return (qN, qD);
        });
    }
}
