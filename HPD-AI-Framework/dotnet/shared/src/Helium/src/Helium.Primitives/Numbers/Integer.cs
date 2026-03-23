using System.Globalization;
using System.Numerics;

namespace Helium.Primitives;

/// <summary>
/// Arbitrary precision integer. Wraps BigInteger, implements the algebraic interface hierarchy.
/// </summary>
public readonly struct Integer :
    IRing<Integer>,
    ICommRing<Integer>,
    IEuclideanDomain<Integer>,
    IGcdDomain<Integer>,
    IOrdered<Integer>,
    INoZeroDivisors<Integer>,
    ICharP<Integer>,
    IComparable<Integer>,
    IFormattable,
    IParsable<Integer>,
    ISpanParsable<Integer>
{
    private readonly BigInteger _value;

    public Integer(BigInteger value) => _value = value;

    public static implicit operator Integer(int value) => new(value);
    public static implicit operator Integer(long value) => new(value);
    public static implicit operator Integer(BigInteger value) => new(value);
    public static explicit operator BigInteger(Integer value) => value._value;

    public static Integer Parse(string s) => new(BigInteger.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture));

    public static Integer Parse(string s, IFormatProvider? provider) =>
        new(BigInteger.Parse(s, NumberStyles.Integer, provider));

    public static bool TryParse(string? s, IFormatProvider? provider, out Integer result)
    {
        if (s is null)
        {
            result = Zero;
            return false;
        }

        if (BigInteger.TryParse(s, NumberStyles.Integer, provider, out var value))
        {
            result = new Integer(value);
            return true;
        }

        result = Zero;
        return false;
    }

    public static Integer Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
        new(BigInteger.Parse(s, NumberStyles.Integer, provider));

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Integer result)
    {
        if (BigInteger.TryParse(s, NumberStyles.Integer, provider, out var value))
        {
            result = new Integer(value);
            return true;
        }

        result = Zero;
        return false;
    }

    // --- Identity elements ---

    public static Integer Zero => new(BigInteger.Zero);
    public static Integer One => new(BigInteger.One);

    static Integer IAdditiveIdentity<Integer, Integer>.AdditiveIdentity => Zero;
    static Integer IMultiplicativeIdentity<Integer, Integer>.MultiplicativeIdentity => One;

    // --- Arithmetic operators ---

    public static Integer operator +(Integer left, Integer right) => new(left._value + right._value);
    public static Integer operator -(Integer left, Integer right) => new(left._value - right._value);
    public static Integer operator *(Integer left, Integer right) => new(left._value * right._value);
    public static Integer operator -(Integer value) => new(-value._value);

    // Division by zero returns zero (total function convention).
    public static Integer operator /(Integer left, Integer right) =>
        right._value.IsZero ? Zero : new(left._value / right._value);

    public static Integer operator %(Integer left, Integer right) =>
        right._value.IsZero ? Zero : new(left._value % right._value);

    // --- IEuclideanDomain ---

    public static (Integer Quotient, Integer Remainder) DivMod(Integer a, Integer b)
    {
        if (b._value.IsZero)
            return (Zero, Zero);
        var (q, r) = BigInteger.DivRem(a._value, b._value);
        return (new(q), new(r));
    }

    // --- IGcdDomain ---

    public static Integer Gcd(Integer a, Integer b) =>
        new(BigInteger.GreatestCommonDivisor(a._value, b._value));

    public static Integer Lcm(Integer a, Integer b)
    {
        if (a._value.IsZero || b._value.IsZero)
            return Zero;
        return new(BigInteger.Abs(a._value / BigInteger.GreatestCommonDivisor(a._value, b._value) * b._value));
    }

    // --- ICharP ---

    public static int Characteristic => 0;

    // --- Comparison operators ---

    public static bool operator ==(Integer left, Integer right) => left._value == right._value;
    public static bool operator !=(Integer left, Integer right) => left._value != right._value;
    public static bool operator <(Integer left, Integer right) => left._value < right._value;
    public static bool operator >(Integer left, Integer right) => left._value > right._value;
    public static bool operator <=(Integer left, Integer right) => left._value <= right._value;
    public static bool operator >=(Integer left, Integer right) => left._value >= right._value;

    public int CompareTo(Integer other) => _value.CompareTo(other._value);

    // --- Equality ---

    public bool Equals(Integer other) => _value == other._value;
    public override bool Equals(object? obj) => obj is Integer other && Equals(other);
    public override int GetHashCode() => _value.GetHashCode();
    public override string ToString() => _value.ToString();

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        return format switch
        {
            null or "" or "L" or "U" => _value.ToString(),
            "M" => $"<mn>{_value}</mn>",
            _ => _value.ToString(format, formatProvider)
        };
    }

    // --- IRing.FromInt override ---

    static Integer IRing<Integer>.FromInt(int n) => new((BigInteger)n);

    // --- Helpers ---

    public bool IsZero => _value.IsZero;
    public bool IsOne => _value.IsOne;
    public Integer Abs() => new(BigInteger.Abs(_value));
    public int Sign => _value.Sign;

    public static Integer Pow(Integer @base, int exponent) =>
        new(BigInteger.Pow(@base._value, exponent));
}
