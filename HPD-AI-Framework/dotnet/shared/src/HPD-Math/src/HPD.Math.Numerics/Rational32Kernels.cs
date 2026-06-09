using HPD.Math.Core;

namespace HPD.Math.Numerics;

/// <summary>
/// Status-returning arithmetic for fixed-width exact rationals.
/// </summary>
public static class Rational32Kernels
{
    public static AlgebraStatus TryCreate(int numerator, int denominator, out Rational32 value)
    {
        if (denominator == 0)
        {
            value = Rational32.Zero;
            return AlgebraStatus.DivisionByZero;
        }

        if (numerator == 0)
        {
            value = Rational32.Zero;
            return AlgebraStatus.Ok;
        }

        long n = numerator;
        long d = denominator;
        if (d < 0)
        {
            n = -n;
            d = -d;
        }

        var gcd = Gcd(Abs(n), d);
        n /= gcd;
        d /= gcd;

        if (!FitsInt32(n) || !FitsInt32(d))
        {
            value = Rational32.Zero;
            return AlgebraStatus.Overflow;
        }

        value = new Rational32((int)n, (int)d, true);
        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryAdd(in Rational32 left, in Rational32 right, out Rational32 destination)
    {
        var numerator = (long)left.Numerator * right.Denominator + (long)right.Numerator * left.Denominator;
        var denominator = (long)left.Denominator * right.Denominator;
        return TryCreateFromInt64(numerator, denominator, out destination);
    }

    public static AlgebraStatus TrySub(in Rational32 left, in Rational32 right, out Rational32 destination)
    {
        var numerator = (long)left.Numerator * right.Denominator - (long)right.Numerator * left.Denominator;
        var denominator = (long)left.Denominator * right.Denominator;
        return TryCreateFromInt64(numerator, denominator, out destination);
    }

    public static AlgebraStatus TryMul(in Rational32 left, in Rational32 right, out Rational32 destination)
    {
        var numerator = (long)left.Numerator * right.Numerator;
        var denominator = (long)left.Denominator * right.Denominator;
        return TryCreateFromInt64(numerator, denominator, out destination);
    }

    public static AlgebraStatus TryDiv(in Rational32 left, in Rational32 right, out Rational32 destination)
    {
        if (right.Numerator == 0)
        {
            destination = Rational32.Zero;
            return AlgebraStatus.DivisionByZero;
        }

        var numerator = (long)left.Numerator * right.Denominator;
        var denominator = (long)left.Denominator * right.Numerator;
        return TryCreateFromInt64(numerator, denominator, out destination);
    }

    public static AlgebraStatus TryNeg(in Rational32 value, out Rational32 destination)
    {
        if (value.Numerator == int.MinValue)
        {
            destination = Rational32.Zero;
            return AlgebraStatus.Overflow;
        }

        destination = new Rational32(-value.Numerator, value.Denominator, true);
        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryInvert(in Rational32 value, out Rational32 destination)
    {
        if (value.Numerator == 0)
        {
            destination = Rational32.Zero;
            return AlgebraStatus.DivisionByZero;
        }

        return TryCreate(value.Denominator, value.Numerator, out destination);
    }

    private static AlgebraStatus TryCreateFromInt64(long numerator, long denominator, out Rational32 value)
    {
        if (denominator == 0)
        {
            value = Rational32.Zero;
            return AlgebraStatus.DivisionByZero;
        }

        if (numerator == 0)
        {
            value = Rational32.Zero;
            return AlgebraStatus.Ok;
        }

        if (denominator < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        var gcd = Gcd(Abs(numerator), denominator);
        numerator /= gcd;
        denominator /= gcd;

        if (!FitsInt32(numerator) || !FitsInt32(denominator))
        {
            value = Rational32.Zero;
            return AlgebraStatus.Overflow;
        }

        value = new Rational32((int)numerator, (int)denominator, true);
        return AlgebraStatus.Ok;
    }

    private static long Gcd(long left, long right)
    {
        while (right != 0)
        {
            var temp = left % right;
            left = right;
            right = temp;
        }

        return left == 0 ? 1 : left;
    }

    private static long Abs(long value) => value < 0 ? -value : value;

    private static bool FitsInt32(long value) => value >= int.MinValue && value <= int.MaxValue;
}
