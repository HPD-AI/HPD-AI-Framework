using System.Numerics;
using System.Text;
using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// Univariate polynomial over a ring R, backed by Finsupp (exponent -> coefficient).
/// Canonical form by construction: no zero coefficients, equality is structural.
/// Multiplication is convolution (not pointwise).
/// </summary>
public readonly struct SparsePolynomial<R> :
    ICommRing<SparsePolynomial<R>>,
    IEquatable<SparsePolynomial<R>>,
    IFormattable
    where R : IRing<R>
{
    private readonly Finsupp<Degree, R> _coeffs;

    internal Finsupp<Degree, R> Coeffs => _coeffs;

    private SparsePolynomial(Finsupp<Degree, R> coeffs)
    {
        _coeffs = coeffs;
    }

    // --- Construction ---

    public static SparsePolynomial<R> Zero => new(Finsupp<Degree, R>.Empty);

    public static SparsePolynomial<R> One => Monomial(0, R.MultiplicativeIdentity);

    public static SparsePolynomial<R> X => Monomial(1, R.MultiplicativeIdentity);

    public static SparsePolynomial<R> C(R value) => Monomial(0, value);

    public static SparsePolynomial<R> Monomial(int degree, R coefficient)
    {
        if (degree < 0) return Zero;
        return new(Finsupp<Degree, R>.Single(new Degree(degree), coefficient));
    }

    public static SparsePolynomial<R> FromCoeffs(params ReadOnlySpan<R> coefficients)
    {
        var pairs = new List<KeyValuePair<Degree, R>>(coefficients.Length);
        for (int i = 0; i < coefficients.Length; i++)
            pairs.Add(new(new Degree(i), coefficients[i]));
        return new(Finsupp<Degree, R>.FromDictionary(pairs));
    }

    // --- Identity elements ---

    static SparsePolynomial<R> IAdditiveIdentity<SparsePolynomial<R>, SparsePolynomial<R>>.AdditiveIdentity => Zero;
    static SparsePolynomial<R> IMultiplicativeIdentity<SparsePolynomial<R>, SparsePolynomial<R>>.MultiplicativeIdentity => One;

    // --- Coefficient access ---

    public R this[int n] => n < 0 ? R.AdditiveIdentity : _coeffs[new Degree(n)];

    // --- Derived properties ---

    public bool IsZero => _coeffs.IsZero;

    public int Degree
    {
        get
        {
            if (IsZero) return -1;
            int max = -1;
            foreach (var k in _coeffs.Support)
                if (k.Value > max) max = k.Value;
            return max;
        }
    }

    public R LeadingCoefficient => IsZero ? R.AdditiveIdentity : this[Degree];

    public IEnumerable<int> Support => _coeffs.Support.Select(k => k.Value);

    // --- Arithmetic ---

    public static SparsePolynomial<R> operator +(SparsePolynomial<R> left, SparsePolynomial<R> right) =>
        new(left._coeffs + right._coeffs);

    public static SparsePolynomial<R> operator -(SparsePolynomial<R> left, SparsePolynomial<R> right) =>
        new(left._coeffs - right._coeffs);

    public static SparsePolynomial<R> operator -(SparsePolynomial<R> p) =>
        new(-p._coeffs);

    /// <summary>
    /// Convolution product: for each pair (e1,c1) in p and (e2,c2) in q,
    /// contribute c1*c2 at exponent e1+e2.
    /// Accumulates into a mutable Dictionary, then builds Finsupp once at the end
    /// to avoid O(n*m) immutable dictionary copies during the hot loop.
    /// </summary>
    public static SparsePolynomial<R> operator *(SparsePolynomial<R> left, SparsePolynomial<R> right)
    {
        if (left.IsZero || right.IsZero)
            return Zero;

        var scratch = new Dictionary<Degree, R>();
        foreach (var e1 in left._coeffs.Support)
        {
            var c1 = left._coeffs[e1];
            foreach (var e2 in right._coeffs.Support)
            {
                var c2 = right._coeffs[e2];
                var key = new Degree(e1.Value + e2.Value);
                scratch[key] = scratch.TryGetValue(key, out var current)
                    ? current + c1 * c2
                    : c1 * c2;
            }
        }
        return new(Finsupp<Degree, R>.FromDictionary(scratch));
    }

    // --- Equality ---

    public static bool operator ==(SparsePolynomial<R> left, SparsePolynomial<R> right) => left._coeffs == right._coeffs;
    public static bool operator !=(SparsePolynomial<R> left, SparsePolynomial<R> right) => !(left == right);
    public bool Equals(SparsePolynomial<R> other) => _coeffs == other._coeffs;
    public override bool Equals(object? obj) => obj is SparsePolynomial<R> other && Equals(other);
    public override int GetHashCode() => _coeffs.GetHashCode();

    public override string ToString() => ToString(null, null);

    public string ToString(string? format, IFormatProvider? provider)
    {
        if (IsZero) return "0";
        if (format == "M")
            return FormatMathML(provider);

        var sb = new StringBuilder();
        bool first = true;

        foreach (var exp in _coeffs.Support.OrderByDescending(x => x.Value))
        {
            var coeff = _coeffs[exp];
            bool isConstant = (exp.Value == 0);
            bool isOne = coeff.Equals(R.MultiplicativeIdentity);
            bool isMinusOne = coeff.Equals(-R.MultiplicativeIdentity);

            if (isConstant)
            {
                var coeffStr = FormatHelpers.FormatElement(coeff, format, provider);
                FormatHelpers.AppendTerm(sb, coeffStr, "", first);
            }
            else if (isOne)
            {
                FormatHelpers.AppendSignedTerm(sb, positive: true, FormatVariable(exp.Value, format), first);
            }
            else if (isMinusOne)
            {
                FormatHelpers.AppendSignedTerm(sb, positive: false, FormatVariable(exp.Value, format), first);
            }
            else
            {
                var coeffStr = FormatHelpers.FormatElement(coeff, format, provider);
                if (format is null or "" or "U" && FormatHelpers.NeedsParentheses(coeffStr))
                    coeffStr = $"({coeffStr})";
                FormatHelpers.AppendTerm(sb, coeffStr, FormatVariable(exp.Value, format), first);
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
        foreach (var exp in _coeffs.Support.OrderByDescending(x => x.Value))
        {
            var coeff = _coeffs[exp];
            bool isConstant = (exp.Value == 0);
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
                body = FormatVariableMathML(exp.Value);
            }
            else if (isMinusOne)
            {
                negative = true;
                body = FormatVariableMathML(exp.Value);
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
                body = $"<mrow>{coeffMathMl}<mo>&#x2062;</mo>{FormatVariableMathML(exp.Value)}</mrow>";
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
/// Euclidean domain operations for SparsePolynomial when R is a field.
/// C# 14 extension block: adds DivMod() and Gcd() conditionally when R : IField.
/// </summary>
public static class SparsePolynomialFieldExtensions
{
    extension<R>(SparsePolynomial<R> self) where R : IField<R>
    {
        /// <summary>
        /// SparsePolynomial long division. Returns (quotient, remainder) where
        /// self == quotient * divisor + remainder, and Degree(remainder) &lt; Degree(divisor).
        /// </summary>
        public (SparsePolynomial<R> Quotient, SparsePolynomial<R> Remainder) DivMod(SparsePolynomial<R> divisor)
        {
            if (divisor.IsZero)
                return (SparsePolynomial<R>.Zero, SparsePolynomial<R>.Zero);

            var remainder = self;
            var quotient = SparsePolynomial<R>.Zero;
            var divisorDeg = divisor.Degree;
            var divisorLC = divisor.LeadingCoefficient;
            var divisorLCInv = R.Invert(divisorLC);

            while (!remainder.IsZero && remainder.Degree >= divisorDeg)
            {
                var coeff = remainder.LeadingCoefficient * divisorLCInv;
                var deg = remainder.Degree - divisorDeg;
                var term = SparsePolynomial<R>.Monomial(deg, coeff);
                quotient = quotient + term;
                remainder = remainder - term * divisor;
            }

            return (quotient, remainder);
        }

        /// <summary>
        /// GCD of two polynomials over a field, via the Euclidean algorithm.
        /// Result is monic (leading coefficient 1) when nonzero.
        /// </summary>
        public SparsePolynomial<R> Gcd(SparsePolynomial<R> other)
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
            return a * SparsePolynomial<R>.C(lcInv);
        }

        /// <summary>
        /// Extended Euclidean algorithm for polynomials over a field.
        /// Returns (gcd, u, v) such that u * self + v * other == gcd.
        /// The gcd is monic when nonzero.
        /// </summary>
        public (SparsePolynomial<R> Gcd, SparsePolynomial<R> U, SparsePolynomial<R> V) ExtendedGcd(SparsePolynomial<R> other)
        {
            var oldR = self;
            var r = other;
            var oldU = SparsePolynomial<R>.One;
            var u = SparsePolynomial<R>.Zero;
            var oldV = SparsePolynomial<R>.Zero;
            var v = SparsePolynomial<R>.One;

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
            var scale = SparsePolynomial<R>.C(lcInv);
            return (oldR * scale, oldU * scale, oldV * scale);
        }
    }
}

/// <summary>
/// Cross-layer extensions: FormalPowerSeries truncation to SparsePolynomial.
/// Lives in Helium.Algebra because Primitives cannot reference Algebra types.
/// </summary>
public static class FormalPowerSeriesTruncateExtensions
{
    extension<R>(FormalPowerSeries<R> self) where R : IField<R>
    {
        /// <summary>
        /// Truncate the power series at degree n, returning a polynomial of degree &lt; n.
        /// </summary>
        public SparsePolynomial<R> Truncate(int n)
        {
            var pairs = new List<KeyValuePair<int, R>>(n);
            for (int i = 0; i < n; i++)
                pairs.Add(new(i, self.Coefficient(i)));
            return SparsePolynomial<R>.FromCoeffs(CoeffsToArray<R>(pairs));
        }
    }

    /// <summary>
    /// Embed a polynomial as a formal power series (finite support).
    /// </summary>
    public static FormalPowerSeries<R> FromSparsePolynomial<R>(SparsePolynomial<R> p)
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
