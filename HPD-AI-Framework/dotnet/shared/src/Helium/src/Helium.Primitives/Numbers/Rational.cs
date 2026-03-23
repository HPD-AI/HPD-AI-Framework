using System.Numerics;

namespace Helium.Primitives;

/// <summary>
/// Exact rational number. Always in canonical form: gcd(|num|, den) == 1, den > 0.
/// </summary>
public readonly struct Rational :
    IField<Rational>,
    IEuclideanDomain<Rational>,
    IOrdered<Rational>,
    INoZeroDivisors<Rational>,
    ICharP<Rational>,
    IComparable<Rational>,
    IFormattable,
    IParsable<Rational>,
    ISpanParsable<Rational>
{
    public Integer Numerator { get; }
    public Integer Denominator { get; }

    private Rational(Integer numerator, Integer denominator)
    {
        Numerator = numerator;
        Denominator = denominator;
    }

    /// <summary>
    /// Creates a rational in canonical form. If den is zero, returns Zero (total function convention).
    /// </summary>
    public static Rational Create(Integer num, Integer den)
    {
        if (den.IsZero)
            return Zero;
        if (num.IsZero)
            return Zero;

        // Sign normalization: denominator always positive.
        if (den.Sign < 0)
        {
            num = -num;
            den = -den;
        }

        // GCD reduction.
        var gcd = Integer.Gcd(num.Abs(), den);
        return new Rational(num / gcd, den / gcd);
    }

    // --- Identity elements ---

    public static Rational Zero => new(Integer.Zero, Integer.One);
    public static Rational One => new(Integer.One, Integer.One);

    static Rational IAdditiveIdentity<Rational, Rational>.AdditiveIdentity => Zero;
    static Rational IMultiplicativeIdentity<Rational, Rational>.MultiplicativeIdentity => One;

    // --- Arithmetic operators ---

    public static Rational operator +(Rational left, Rational right) =>
        Create(
            left.Numerator * right.Denominator + right.Numerator * left.Denominator,
            left.Denominator * right.Denominator);

    public static Rational operator -(Rational left, Rational right) =>
        Create(
            left.Numerator * right.Denominator - right.Numerator * left.Denominator,
            left.Denominator * right.Denominator);

    public static Rational operator *(Rational left, Rational right) =>
        Create(left.Numerator * right.Numerator, left.Denominator * right.Denominator);

    public static Rational operator /(Rational left, Rational right) =>
        left * Invert(right);

    public static Rational operator -(Rational value) =>
        new(-value.Numerator, value.Denominator);

    // --- IField ---

    /// <summary>
    /// Multiplicative inverse. Invert(0) returns 0 (total function convention).
    /// </summary>
    public static Rational Invert(Rational a)
    {
        if (a.Numerator.IsZero)
            return Zero;
        return a.Numerator.Sign > 0
            ? new Rational(a.Denominator, a.Numerator)
            : new Rational(-a.Denominator, -a.Numerator);
    }

    // --- IEuclideanDomain (every field is a Euclidean domain) ---

    /// <summary>Field division: DivMod(a, b) = (a/b, 0). Remainder is always zero.</summary>
    public static (Rational Quotient, Rational Remainder) DivMod(Rational a, Rational b) =>
        (a / b, Zero);

    /// <summary>GCD in a field: 1 when both nonzero, else the nonzero element, else 0.</summary>
    public static Rational Gcd(Rational a, Rational b)
    {
        if (a.IsZero) return b;
        if (b.IsZero) return a;
        return One;
    }

    /// <summary>LCM in a field: a*b when both nonzero (trivial since GCD = 1).</summary>
    public static Rational Lcm(Rational a, Rational b) =>
        a.IsZero || b.IsZero ? Zero : One;

    // --- IRing.FromInt override ---

    static Rational IRing<Rational>.FromInt(int n) => new((Integer)n, Integer.One);

    // --- ICharP ---

    public static int Characteristic => 0;

    // --- Comparison operators ---

    // a/b vs c/d => compare a*d vs c*b (since denominators are positive).
    public static bool operator ==(Rational left, Rational right) =>
        left.Numerator == right.Numerator && left.Denominator == right.Denominator;

    public static bool operator !=(Rational left, Rational right) => !(left == right);

    public static bool operator <(Rational left, Rational right) =>
        left.Numerator * right.Denominator < right.Numerator * left.Denominator;

    public static bool operator >(Rational left, Rational right) => right < left;
    public static bool operator <=(Rational left, Rational right) => !(right < left);
    public static bool operator >=(Rational left, Rational right) => !(left < right);

    public int CompareTo(Rational other) =>
        (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);

    // --- Equality ---

    public bool Equals(Rational other) => this == other;
    public override bool Equals(object? obj) => obj is Rational other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);
    public override string ToString() => Denominator.IsOne ? $"{Numerator}" : $"{Numerator}/{Denominator}";

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        if (Denominator.IsOne)
            return Numerator.ToString(format, formatProvider);

        return format switch
        {
            "L" => FormatLatex(),
            "M" => $"<mfrac><mn>{Numerator}</mn><mn>{Denominator}</mn></mfrac>",
            _ => $"{Numerator}/{Denominator}"
        };
    }

    private string FormatLatex()
    {
        if (Numerator.Sign < 0)
            return $@"-\frac{{{Numerator.Abs()}}}{{{Denominator}}}";
        return $@"\frac{{{Numerator}}}{{{Denominator}}}";
    }

    // --- Helpers ---

    public bool IsZero => Numerator.IsZero;
    public static Rational FromInteger(Integer n) => new(n, Integer.One);
    public static implicit operator Rational(int n) => new((Integer)n, Integer.One);

    // --- Parsing (IParsable / ISpanParsable) ---

    public static Rational Parse(string s, IFormatProvider? provider) =>
        Parse(s.AsSpan(), provider);

    public static bool TryParse(string? s, IFormatProvider? provider, out Rational result)
    {
        if (s is null)
        {
            result = Zero;
            return false;
        }

        return TryParse(s.AsSpan(), provider, out result);
    }

    public static Rational Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
        if (TryParse(s, provider, out var result))
            return result;
        throw new FormatException("Invalid rational literal.");
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Rational result)
    {
        s = Trim(s);
        if (s.Length == 0)
        {
            result = Zero;
            return false;
        }

        int slash = s.IndexOf('/');
        if (slash < 0)
        {
            if (!Integer.TryParse(s, provider, out var n))
            {
                result = Zero;
                return false;
            }

            result = FromInteger(n);
            return true;
        }

        var numSpan = Trim(s[..slash]);
        var denSpan = Trim(s[(slash + 1)..]);
        if (numSpan.Length == 0 || denSpan.Length == 0)
        {
            result = Zero;
            return false;
        }

        if (!Integer.TryParse(numSpan, provider, out var num) ||
            !Integer.TryParse(denSpan, provider, out var den))
        {
            result = Zero;
            return false;
        }

        result = Create(num, den);
        return true;
    }

    private static ReadOnlySpan<char> Trim(ReadOnlySpan<char> s)
    {
        int start = 0;
        while (start < s.Length && char.IsWhiteSpace(s[start]))
            start++;

        int end = s.Length - 1;
        while (end >= start && char.IsWhiteSpace(s[end]))
            end--;

        return s[start..(end + 1)];
    }
}
