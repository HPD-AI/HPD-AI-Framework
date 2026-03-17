using System.Numerics;
using System.Text;
using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// Rational function: ratio of two univariate polynomials p(x)/q(x).
/// Arithmetic is delegated to Localization&lt;Polynomial&lt;R&gt;&gt; — the single
/// authoritative implementation of fraction arithmetic. This type adds
/// its own formatting (LaTeX/MathML/Unicode) and a named public API.
/// </summary>
public readonly struct RationalFunction<R> :
    ICommRing<RationalFunction<R>>,
    IEquatable<RationalFunction<R>>,
    IFormattable
    where R : IRing<R>
{
    private readonly Localization<Polynomial<R>> _inner;

    private RationalFunction(Localization<Polynomial<R>> inner) => _inner = inner;

    public Polynomial<R> Numerator   => _inner.Numerator;
    public Polynomial<R> Denominator => _inner.Denominator;

    // Expose the normalizer so extension methods can forward it.
    internal Func<Polynomial<R>, Polynomial<R>, (Polynomial<R>, Polynomial<R>)>? Normalize =>
        _inner.Normalize;

    // --- Construction ---

    public static RationalFunction<R> Create(Polynomial<R> numerator, Polynomial<R> denominator,
        Func<Polynomial<R>, Polynomial<R>, (Polynomial<R>, Polynomial<R>)>? normalize = null) =>
        new(Localization<Polynomial<R>>.Create(numerator, denominator, normalize));

    public static RationalFunction<R> FromPolynomial(Polynomial<R> p) =>
        Create(p, Polynomial<R>.One);

    public static RationalFunction<R> Zero => Create(Polynomial<R>.Zero, Polynomial<R>.One);
    public static RationalFunction<R> One  => Create(Polynomial<R>.One,  Polynomial<R>.One);

    public bool IsZero => Numerator.IsZero;

    // --- Identity elements ---

    static RationalFunction<R> IAdditiveIdentity<RationalFunction<R>, RationalFunction<R>>.AdditiveIdentity       => Zero;
    static RationalFunction<R> IMultiplicativeIdentity<RationalFunction<R>, RationalFunction<R>>.MultiplicativeIdentity => One;

    // --- Arithmetic: delegate to Localization<Polynomial<R>> ---

    public static RationalFunction<R> operator +(RationalFunction<R> left, RationalFunction<R> right) =>
        new(left._inner + right._inner);

    public static RationalFunction<R> operator -(RationalFunction<R> left, RationalFunction<R> right) =>
        new(left._inner - right._inner);

    public static RationalFunction<R> operator *(RationalFunction<R> left, RationalFunction<R> right) =>
        new(left._inner * right._inner);

    public static RationalFunction<R> operator -(RationalFunction<R> value) =>
        new(-value._inner);

    // --- Equality (delegates to Localization cross-multiply) ---

    public static bool operator ==(RationalFunction<R> left, RationalFunction<R> right) =>
        left._inner == right._inner;

    public static bool operator !=(RationalFunction<R> left, RationalFunction<R> right) => !(left == right);

    public bool Equals(RationalFunction<R> other) => _inner == other._inner;
    public override bool Equals(object? obj) => obj is RationalFunction<R> other && Equals(other);
    public override int GetHashCode() => _inner.GetHashCode();

    // --- Formatting ---

    public override string ToString() => ToString(null, null);

    public string ToString(string? format, IFormatProvider? provider)
    {
        if (IsZero) return "0";

        if (Denominator.Equals(Polynomial<R>.One))
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
            if (gcd.IsZero || gcd.Equals(Polynomial<R>.One))
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
    public static RationalFunction<R> Of<R>(Polynomial<R> numerator, Polynomial<R> denominator)
        where R : IField<R>
    {
        return RationalFunction<R>.Create(numerator, denominator, (n, d) =>
        {
            if (d.IsZero) return (Polynomial<R>.Zero, Polynomial<R>.One);
            if (n.IsZero) return (Polynomial<R>.Zero, Polynomial<R>.One);

            var gcd = n.Gcd(d);
            if (gcd.IsZero || gcd.Equals(Polynomial<R>.One))
                return (n, d);

            var (qN, _) = n.DivMod(gcd);
            var (qD, _) = d.DivMod(gcd);
            return (qN, qD);
        });
    }
}
