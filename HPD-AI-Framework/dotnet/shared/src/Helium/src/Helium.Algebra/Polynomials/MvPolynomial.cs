using System.Numerics;
using System.Text;
using Helium.Primitives;

namespace Helium.Algebra;

/// <summary>
/// Monomial exponent vector: maps variable indices to exponents.
/// x^2 * y^3 = Monomial({0: 2, 1: 3}). Addition = exponent-wise addition (monomial multiplication).
/// </summary>
public readonly struct Monomial :
    IEquatable<Monomial>,
    IComparable<Monomial>,
    IAdditiveIdentity<Monomial, Monomial>,
    IEqualityOperators<Monomial, Monomial, bool>,
    IFormattable
{
    private readonly Finsupp<int, Integer> _exponents;

    internal Finsupp<int, Integer> Exponents => _exponents;

    private Monomial(Finsupp<int, Integer> exponents)
    {
        _exponents = exponents;
    }

    // --- Construction ---

    public static Monomial One => new(Finsupp<int, Integer>.Empty);
    public static Monomial AdditiveIdentity => One;

    public static Monomial Variable(int index) =>
        new(Finsupp<int, Integer>.Single(index, Integer.One));

    public static Monomial FromExponents(IEnumerable<KeyValuePair<int, Integer>> exponents) =>
        new(Finsupp<int, Integer>.FromDictionary(exponents));

    // --- Access ---

    public Integer this[int variableIndex] => _exponents[variableIndex];

    public int TotalDegree
    {
        get
        {
            int sum = 0;
            foreach (var v in _exponents.Support)
                sum += (int)(System.Numerics.BigInteger)(Integer)_exponents[v];
            return sum;
        }
    }

    public IEnumerable<int> Variables => _exponents.Support;

    // --- Monomial multiplication = exponent addition ---

    public static Monomial operator +(Monomial left, Monomial right) =>
        new(left._exponents + right._exponents);

    /// <summary>
    /// Returns true if this monomial divides other, i.e., every exponent of this
    /// is less than or equal to the corresponding exponent of other.
    /// </summary>
    public bool Divides(Monomial other)
    {
        foreach (var v in _exponents.Support)
        {
            if (_exponents[v] > other._exponents[v])
                return false;
        }
        return true;
    }

    /// <summary>
    /// Monomial division: other / this. Assumes this.Divides(other).
    /// Result has exponents = other.exponents - this.exponents.
    /// </summary>
    public static Monomial operator -(Monomial other, Monomial divisor) =>
        new(other._exponents - divisor._exponents);

    // --- Comparison (graded lexicographic) ---

    public int CompareTo(Monomial other)
    {
        int degCompare = TotalDegree.CompareTo(other.TotalDegree);
        if (degCompare != 0) return degCompare;

        // Lexicographic on variable indices.
        var allVars = _exponents.Support.Concat(other._exponents.Support).Distinct().OrderBy(x => x);
        foreach (var v in allVars)
        {
            int cmp = _exponents[v].CompareTo(other._exponents[v]);
            if (cmp != 0) return cmp;
        }
        return 0;
    }

    // --- Equality ---

    public bool Equals(Monomial other) => _exponents == other._exponents;
    public override bool Equals(object? obj) => obj is Monomial other && Equals(other);
    public override int GetHashCode() => _exponents.GetHashCode();

    public static bool operator ==(Monomial left, Monomial right) => left.Equals(right);
    public static bool operator !=(Monomial left, Monomial right) => !left.Equals(right);

    public override string ToString() => ToString(null, null);

    public string ToString(string? format, IFormatProvider? provider)
    {
        if (_exponents.IsZero) return "1";
        if (format == "M")
            return FormatMathML();

        var sb = new StringBuilder();
        foreach (var v in _exponents.Support.OrderBy(v => v))
        {
            var varName = VariableName(v);
            int exp = (int)(BigInteger)(Integer)_exponents[v];

            if (exp == 1)
            {
                sb.Append(varName);
            }
            else
            {
                sb.Append(format switch
                {
                    "L" => $"{varName}^{{{exp}}}",
                    "U" => varName + FormatHelpers.ToSuperscript(exp),
                    _ => $"{varName}^{exp}"
                });
            }
        }
        return sb.ToString();
    }

    private string FormatMathML()
    {
        if (_exponents.IsZero) return "<mn>1</mn>";

        var factors = new List<string>();
        foreach (var v in _exponents.Support.OrderBy(v => v))
        {
            var varName = VariableName(v);
            int exp = (int)(BigInteger)(Integer)_exponents[v];
            factors.Add(exp == 1
                ? $"<mi>{varName}</mi>"
                : $"<msup><mi>{varName}</mi><mn>{exp}</mn></msup>");
        }

        if (factors.Count == 1)
            return factors[0];

        return "<mrow>" + string.Join("<mo>&#x2062;</mo>", factors) + "</mrow>";
    }

    private static string VariableName(int index) => index switch
    {
        0 => "x",
        1 => "y",
        2 => "z",
        _ => $"x{index}"
    };
}

/// <summary>
/// Multivariate polynomial over a commutative ring R.
/// Backed by Finsupp(Monomial, R) with convolution multiplication.
/// </summary>
public readonly struct MvPolynomial<R> :
    ICommRing<MvPolynomial<R>>,
    IEquatable<MvPolynomial<R>>,
    IFormattable
    where R : ICommRing<R>
{
    private readonly Finsupp<Monomial, R> _coeffs;

    private MvPolynomial(Finsupp<Monomial, R> coeffs)
    {
        _coeffs = coeffs;
    }

    // --- Construction ---

    public static MvPolynomial<R> Zero => new(Finsupp<Monomial, R>.Empty);
    public static MvPolynomial<R> One => new(Finsupp<Monomial, R>.Single(Monomial.One, R.MultiplicativeIdentity));

    public static MvPolynomial<R> C(R value) =>
        new(Finsupp<Monomial, R>.Single(Monomial.One, value));

    public static MvPolynomial<R> Var(int index) =>
        new(Finsupp<Monomial, R>.Single(Monomial.Variable(index), R.MultiplicativeIdentity));

    public static MvPolynomial<R> Term(Monomial monomial, R coefficient) =>
        new(Finsupp<Monomial, R>.Single(monomial, coefficient));

    // --- Identity elements ---

    static MvPolynomial<R> IAdditiveIdentity<MvPolynomial<R>, MvPolynomial<R>>.AdditiveIdentity => Zero;
    static MvPolynomial<R> IMultiplicativeIdentity<MvPolynomial<R>, MvPolynomial<R>>.MultiplicativeIdentity => One;

    // --- Access ---

    public R this[Monomial m] => _coeffs[m];
    public bool IsZero => _coeffs.IsZero;
    public IEnumerable<Monomial> Support => _coeffs.Support;

    internal Finsupp<Monomial, R> Coeffs => _coeffs;

    /// <summary>
    /// The leading monomial under graded lexicographic order (the largest monomial in the support).
    /// </summary>
    public Monomial LeadingMonomial
    {
        get
        {
            if (IsZero) return Monomial.One;
            Monomial max = default;
            bool first = true;
            foreach (var m in _coeffs.Support)
            {
                if (first || m.CompareTo(max) > 0)
                {
                    max = m;
                    first = false;
                }
            }
            return max;
        }
    }

    /// <summary>
    /// The coefficient of the leading monomial.
    /// </summary>
    public R LeadingCoefficient => IsZero ? R.AdditiveIdentity : _coeffs[LeadingMonomial];

    // --- Arithmetic ---

    public static MvPolynomial<R> operator +(MvPolynomial<R> left, MvPolynomial<R> right) =>
        new(left._coeffs + right._coeffs);

    public static MvPolynomial<R> operator -(MvPolynomial<R> left, MvPolynomial<R> right) =>
        new(left._coeffs - right._coeffs);

    public static MvPolynomial<R> operator -(MvPolynomial<R> p) =>
        new(-p._coeffs);

    /// <summary>
    /// Convolution: for each pair of terms, multiply coefficients and add exponent vectors.
    /// </summary>
    public static MvPolynomial<R> operator *(MvPolynomial<R> left, MvPolynomial<R> right)
    {
        if (left.IsZero || right.IsZero)
            return Zero;

        var result = Finsupp<Monomial, R>.Empty;
        foreach (var m1 in left._coeffs.Support)
        {
            var c1 = left._coeffs[m1];
            foreach (var m2 in right._coeffs.Support)
            {
                var c2 = right._coeffs[m2];
                var key = m1 + m2;
                var current = result[key];
                result = result.Set(key, current + c1 * c2);
            }
        }
        return new(result);
    }

    // --- Equality ---

    public static bool operator ==(MvPolynomial<R> left, MvPolynomial<R> right) => left._coeffs == right._coeffs;
    public static bool operator !=(MvPolynomial<R> left, MvPolynomial<R> right) => !(left == right);
    public bool Equals(MvPolynomial<R> other) => _coeffs == other._coeffs;
    public override bool Equals(object? obj) => obj is MvPolynomial<R> other && Equals(other);
    public override int GetHashCode() => _coeffs.GetHashCode();

    public override string ToString() => ToString(null, null);

    public string ToString(string? format, IFormatProvider? provider)
    {
        if (IsZero) return "0";
        if (format == "M")
            return FormatMathML(provider);

        var sb = new StringBuilder();
        bool first = true;

        foreach (var monomial in _coeffs.Support.OrderByDescending(m => m))
        {
            var coeff = _coeffs[monomial];
            bool isConstant = monomial.Equals(Monomial.One);
            bool isOne = coeff.Equals(R.MultiplicativeIdentity);
            bool isMinusOne = coeff.Equals(-R.MultiplicativeIdentity);

            if (isConstant)
            {
                var coeffStr = FormatHelpers.FormatElement(coeff, format, provider);
                FormatHelpers.AppendTerm(sb, coeffStr, "", first);
            }
            else if (isOne)
            {
                FormatHelpers.AppendSignedTerm(sb, positive: true, monomial.ToString(format, provider), first);
            }
            else if (isMinusOne)
            {
                FormatHelpers.AppendSignedTerm(sb, positive: false, monomial.ToString(format, provider), first);
            }
            else
            {
                var coeffStr = FormatHelpers.FormatElement(coeff, format, provider);
                if (format is null or "" or "U" && FormatHelpers.NeedsParentheses(coeffStr))
                    coeffStr = $"({coeffStr})";
                FormatHelpers.AppendTerm(sb, coeffStr, monomial.ToString(format, provider), first);
            }
            first = false;
        }

        return sb.ToString();
    }

    private string FormatMathML(IFormatProvider? provider)
    {
        var sb = new StringBuilder();
        sb.Append("<mrow>");

        bool first = true;
        foreach (var monomial in _coeffs.Support.OrderByDescending(m => m))
        {
            var coeff = _coeffs[monomial];
            bool isConstant = monomial.Equals(Monomial.One);
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
                body = monomial.ToString("M", provider);
            }
            else if (isMinusOne)
            {
                negative = true;
                body = monomial.ToString("M", provider);
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
                body = $"<mrow>{coeffMathMl}<mo>&#x2062;</mo>{monomial.ToString("M", provider)}</mrow>";
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

}
