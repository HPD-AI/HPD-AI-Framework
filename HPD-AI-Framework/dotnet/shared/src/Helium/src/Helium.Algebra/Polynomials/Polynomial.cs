using System.Numerics;
using System.Text;
using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// Univariate polynomial over a ring R, backed by Finsupp (exponent -> coefficient).
/// Canonical form by construction: no zero coefficients, equality is structural.
/// Multiplication is convolution (not pointwise).
/// </summary>
public readonly struct Polynomial<R> :
    ICommRing<Polynomial<R>>,
    IEquatable<Polynomial<R>>,
    IFormattable
    where R : IRing<R>
{
    private readonly Finsupp<int, R> _coeffs;

    internal Finsupp<int, R> Coeffs => _coeffs;

    private Polynomial(Finsupp<int, R> coeffs)
    {
        _coeffs = coeffs;
    }

    // --- Construction ---

    public static Polynomial<R> Zero => new(Finsupp<int, R>.Empty);

    public static Polynomial<R> One => Monomial(0, R.MultiplicativeIdentity);

    public static Polynomial<R> X => Monomial(1, R.MultiplicativeIdentity);

    public static Polynomial<R> C(R value) => Monomial(0, value);

    public static Polynomial<R> Monomial(int degree, R coefficient)
    {
        if (degree < 0) return Zero;
        return new(Finsupp<int, R>.Single(degree, coefficient));
    }

    public static Polynomial<R> FromCoeffs(params ReadOnlySpan<R> coefficients)
    {
        var pairs = new List<KeyValuePair<int, R>>(coefficients.Length);
        for (int i = 0; i < coefficients.Length; i++)
            pairs.Add(new(i, coefficients[i]));
        return new(Finsupp<int, R>.FromDictionary(pairs));
    }

    // --- Identity elements ---

    static Polynomial<R> IAdditiveIdentity<Polynomial<R>, Polynomial<R>>.AdditiveIdentity => Zero;
    static Polynomial<R> IMultiplicativeIdentity<Polynomial<R>, Polynomial<R>>.MultiplicativeIdentity => One;

    // --- Coefficient access ---

    public R this[int n] => n < 0 ? R.AdditiveIdentity : _coeffs[n];

    // --- Derived properties ---

    public bool IsZero => _coeffs.IsZero;

    public int Degree
    {
        get
        {
            if (IsZero) return -1;
            int max = -1;
            foreach (var k in _coeffs.Support)
                if (k > max) max = k;
            return max;
        }
    }

    public R LeadingCoefficient => IsZero ? R.AdditiveIdentity : this[Degree];

    public IEnumerable<int> Support => _coeffs.Support;

    // --- Arithmetic ---

    public static Polynomial<R> operator +(Polynomial<R> left, Polynomial<R> right) =>
        new(left._coeffs + right._coeffs);

    public static Polynomial<R> operator -(Polynomial<R> left, Polynomial<R> right) =>
        new(left._coeffs - right._coeffs);

    public static Polynomial<R> operator -(Polynomial<R> p) =>
        new(-p._coeffs);

    /// <summary>
    /// Convolution product: for each pair (e1,c1) in p and (e2,c2) in q,
    /// contribute c1*c2 at exponent e1+e2.
    /// Accumulates into a mutable Dictionary, then builds Finsupp once at the end
    /// to avoid O(n*m) immutable dictionary copies during the hot loop.
    /// </summary>
    public static Polynomial<R> operator *(Polynomial<R> left, Polynomial<R> right)
    {
        if (left.IsZero || right.IsZero)
            return Zero;

        var scratch = new Dictionary<int, R>();
        foreach (var e1 in left._coeffs.Support)
        {
            var c1 = left._coeffs[e1];
            foreach (var e2 in right._coeffs.Support)
            {
                var c2 = right._coeffs[e2];
                var key = e1 + e2;
                scratch[key] = scratch.TryGetValue(key, out var current)
                    ? current + c1 * c2
                    : c1 * c2;
            }
        }
        return new(Finsupp<int, R>.FromDictionary(scratch));
    }

    // --- Equality ---

    public static bool operator ==(Polynomial<R> left, Polynomial<R> right) => left._coeffs == right._coeffs;
    public static bool operator !=(Polynomial<R> left, Polynomial<R> right) => !(left == right);
    public bool Equals(Polynomial<R> other) => _coeffs == other._coeffs;
    public override bool Equals(object? obj) => obj is Polynomial<R> other && Equals(other);
    public override int GetHashCode() => _coeffs.GetHashCode();

    public override string ToString() => ToString(null, null);

    public string ToString(string? format, IFormatProvider? provider)
    {
        if (IsZero) return "0";
        if (format == "M")
            return FormatMathML(provider);

        var sb = new StringBuilder();
        bool first = true;

        foreach (var exp in _coeffs.Support.OrderByDescending(x => x))
        {
            var coeff = _coeffs[exp];
            bool isConstant = (exp == 0);
            bool isOne = coeff.Equals(R.MultiplicativeIdentity);
            bool isMinusOne = coeff.Equals(-R.MultiplicativeIdentity);

            if (isConstant)
            {
                var coeffStr = FormatHelpers.FormatElement(coeff, format, provider);
                FormatHelpers.AppendTerm(sb, coeffStr, "", first);
            }
            else if (isOne)
            {
                FormatHelpers.AppendSignedTerm(sb, positive: true, FormatVariable(exp, format), first);
            }
            else if (isMinusOne)
            {
                FormatHelpers.AppendSignedTerm(sb, positive: false, FormatVariable(exp, format), first);
            }
            else
            {
                var coeffStr = FormatHelpers.FormatElement(coeff, format, provider);
                if (format is null or "" or "U" && FormatHelpers.NeedsParentheses(coeffStr))
                    coeffStr = $"({coeffStr})";
                FormatHelpers.AppendTerm(sb, coeffStr, FormatVariable(exp, format), first);
            }
            first = false;
        }

        return sb.ToString();
    }

    private static string FormatVariable(int exp, string? format)
    {
        if (exp == 0) return "";
        if (exp == 1) return "x";

        return format switch
        {
            "L" => $"x^{{{exp}}}",
            "U" => "x" + FormatHelpers.ToSuperscript(exp),
            "M" => $"<msup><mi>x</mi><mn>{exp}</mn></msup>",
            _ => $"x^{exp}"
        };
    }

    private string FormatMathML(IFormatProvider? provider)
    {
        var sb = new StringBuilder();
        sb.Append("<mrow>");

        bool first = true;
        foreach (var exp in _coeffs.Support.OrderByDescending(x => x))
        {
            var coeff = _coeffs[exp];
            bool isConstant = (exp == 0);
            bool isOne = coeff.Equals(R.MultiplicativeIdentity);
            bool isMinusOne = coeff.Equals(-R.MultiplicativeIdentity);

            bool negative = false;
            string body;
            if (isConstant)
            {
                if (isMinusOne)
                {
                    negative = true;
                    body = "<mn>1</mn>";
                }
                else if (!isOne && FormatHelpers.IsNegativeLike(coeff, provider))
                {
                    negative = true;
                    body = FormatHelpers.FormatElement(-coeff, "M", provider);
                }
                else
                {
                    body = FormatHelpers.FormatElement(coeff, "M", provider);
                }
            }
            else if (isOne)
            {
                body = FormatVariableMathML(exp);
            }
            else if (isMinusOne)
            {
                negative = true;
                body = FormatVariableMathML(exp);
            }
            else
            {
                var displayCoeff = coeff;
                if (FormatHelpers.IsNegativeLike(coeff, provider))
                {
                    negative = true;
                    displayCoeff = -coeff;
                }

                var coeffMathMl = FormatHelpers.FormatElement(displayCoeff, "M", provider);
                body = $"<mrow>{coeffMathMl}<mo>&#x2062;</mo>{FormatVariableMathML(exp)}</mrow>";
            }

            if (first)
            {
                if (negative) sb.Append("<mo>-</mo>");
            }
            else
            {
                sb.Append(negative ? "<mo>-</mo>" : "<mo>+</mo>");
            }

            sb.Append(body);
            first = false;
        }

        sb.Append("</mrow>");
        return sb.ToString();
    }

    private static string FormatVariableMathML(int exp) =>
        exp == 1 ? "<mi>x</mi>" : $"<msup><mi>x</mi><mn>{exp}</mn></msup>";
}

/// <summary>
/// Euclidean domain operations for Polynomial when R is a field.
/// C# 14 extension block: adds DivMod() and Gcd() conditionally when R : IField.
/// </summary>
public static class PolynomialFieldExtensions
{
    extension<R>(Polynomial<R> self) where R : IField<R>
    {
        /// <summary>
        /// Polynomial long division. Returns (quotient, remainder) where
        /// self == quotient * divisor + remainder, and Degree(remainder) &lt; Degree(divisor).
        /// </summary>
        public (Polynomial<R> Quotient, Polynomial<R> Remainder) DivMod(Polynomial<R> divisor)
        {
            if (divisor.IsZero)
                return (Polynomial<R>.Zero, Polynomial<R>.Zero);

            var remainder = self;
            var quotient = Polynomial<R>.Zero;
            var divisorDeg = divisor.Degree;
            var divisorLC = divisor.LeadingCoefficient;
            var divisorLCInv = R.Invert(divisorLC);

            while (!remainder.IsZero && remainder.Degree >= divisorDeg)
            {
                var coeff = remainder.LeadingCoefficient * divisorLCInv;
                var deg = remainder.Degree - divisorDeg;
                var term = Polynomial<R>.Monomial(deg, coeff);
                quotient = quotient + term;
                remainder = remainder - term * divisor;
            }

            return (quotient, remainder);
        }

        /// <summary>
        /// GCD of two polynomials over a field, via the Euclidean algorithm.
        /// Result is monic (leading coefficient 1) when nonzero.
        /// </summary>
        public Polynomial<R> Gcd(Polynomial<R> other)
        {
            var a = self;
            var b = other;
            while (!b.IsZero)
            {
                var (_, r) = a.DivMod(b);
                a = b;
                b = r;
            }

            if (a.IsZero) return a;

            // Make monic.
            var lcInv = R.Invert(a.LeadingCoefficient);
            return a * Polynomial<R>.C(lcInv);
        }

        /// <summary>
        /// Extended Euclidean algorithm for polynomials over a field.
        /// Returns (gcd, u, v) such that u * self + v * other == gcd.
        /// The gcd is monic when nonzero.
        /// </summary>
        public (Polynomial<R> Gcd, Polynomial<R> U, Polynomial<R> V) ExtendedGcd(Polynomial<R> other)
        {
            var oldR = self;
            var r = other;
            var oldU = Polynomial<R>.One;
            var u = Polynomial<R>.Zero;
            var oldV = Polynomial<R>.Zero;
            var v = Polynomial<R>.One;

            while (!r.IsZero)
            {
                var (q, rem) = oldR.DivMod(r);
                (oldR, r) = (r, rem);
                (oldU, u) = (u, oldU - q * u);
                (oldV, v) = (v, oldV - q * v);
            }

            if (oldR.IsZero) return (oldR, oldU, oldV);

            // Normalize to monic gcd.
            var lcInv = R.Invert(oldR.LeadingCoefficient);
            var scale = Polynomial<R>.C(lcInv);
            return (oldR * scale, oldU * scale, oldV * scale);
        }
    }
}

/// <summary>
/// Cross-layer extensions: FormalPowerSeries truncation to Polynomial.
/// Lives in Helium.Algebra because Primitives cannot reference Algebra types.
/// </summary>
public static class FormalPowerSeriesTruncateExtensions
{
    extension<R>(FormalPowerSeries<R> self) where R : IField<R>
    {
        /// <summary>
        /// Truncate the power series at degree n, returning a polynomial of degree &lt; n.
        /// </summary>
        public Polynomial<R> Truncate(int n)
        {
            var pairs = new List<KeyValuePair<int, R>>(n);
            for (int i = 0; i < n; i++)
                pairs.Add(new(i, self.Coefficient(i)));
            return Polynomial<R>.FromCoeffs(CoeffsToArray<R>(pairs));
        }
    }

    /// <summary>
    /// Embed a polynomial as a formal power series (finite support).
    /// </summary>
    public static FormalPowerSeries<R> FromPolynomial<R>(Polynomial<R> p)
        where R : IField<R>
    {
        return FormalPowerSeries<R>.FromGenerator(n =>
            n < 0 || n > p.Degree ? R.AdditiveIdentity : p[n]);
    }

    private static R[] CoeffsToArray<R>(List<KeyValuePair<int, R>> coeffs)
        where R : IRing<R>
    {
        if (coeffs.Count == 0)
            return [];

        int maxDeg = 0;
        foreach (var kv in coeffs)
            if (kv.Key > maxDeg) maxDeg = kv.Key;

        var result = new R[maxDeg + 1];
        Array.Fill(result, R.AdditiveIdentity);
        foreach (var kv in coeffs)
            result[kv.Key] = kv.Value;
        return result;
    }
}
