using System.Numerics;

namespace Helium.Primitives;

public static class IntegerNumberTheory
{
    public static FiniteList<Integer> Divisors(Integer n)
    {
        var value = BigInteger.Abs((BigInteger)n);
        if (value.IsZero)
            return FiniteList<Integer>.Empty;

        var result = new List<Integer>();
        for (var d = BigInteger.One; d * d <= value; d++)
        {
            if (value % d != BigInteger.Zero)
                continue;

            result.Add((Integer)d);
            var other = value / d;
            if (other != d)
                result.Add((Integer)other);
        }

        return FiniteList<Integer>.FromEnumerable(result).Sort();
    }

    public static FiniteList<Pair<Integer, Nat>> TrialDivisionFactor(Integer n)
    {
        var value = BigInteger.Abs((BigInteger)n);
        if (value < 2)
            return FiniteList<Pair<Integer, Nat>>.Empty;

        var factors = new List<Pair<Integer, Nat>>();
        var p = new BigInteger(2);
        while (p * p <= value)
        {
            var exponent = 0;
            while (value % p == BigInteger.Zero)
            {
                value /= p;
                exponent++;
            }

            if (exponent > 0)
                factors.Add(new Pair<Integer, Nat>((Integer)p, new Nat(exponent)));

            p = p == 2 ? 3 : p + 2;
        }

        if (value > 1)
            factors.Add(new Pair<Integer, Nat>((Integer)value, new Nat(1)));

        return FiniteList<Pair<Integer, Nat>>.FromEnumerable(factors);
    }

    public static Integer PowMod(Integer value, Integer exponent, Integer modulus)
    {
        var mod = (BigInteger)modulus;
        if (mod.IsZero)
            return Integer.Zero;

        var exp = (BigInteger)exponent;
        if (exp.Sign < 0)
            throw new ArgumentOutOfRangeException(nameof(exponent), "Modular exponent must be nonnegative.");

        var result = BigInteger.One % mod;
        var b = Mod((BigInteger)value, mod);
        while (exp > BigInteger.Zero)
        {
            if (!exp.IsEven)
                result = Mod(result * b, mod);
            b = Mod(b * b, mod);
            exp >>= 1;
        }

        return (Integer)result;
    }

    public static Integer? ModInverse(Integer value, Integer modulus)
    {
        var mod = (BigInteger)modulus;
        if (mod.IsZero)
            return null;

        var a = Mod((BigInteger)value, mod);
        var t = BigInteger.Zero;
        var newT = BigInteger.One;
        var r = BigInteger.Abs(mod);
        var newR = a;

        while (newR != BigInteger.Zero)
        {
            var quotient = r / newR;
            (t, newT) = (newT, t - quotient * newT);
            (r, newR) = (newR, r - quotient * newR);
        }

        if (r != BigInteger.One)
            return null;

        if (t.Sign < 0)
            t += BigInteger.Abs(mod);

        return (Integer)t;
    }

    public static Integer Totient(Integer n)
    {
        var value = BigInteger.Abs((BigInteger)n);
        if (value.IsZero)
            return Integer.Zero;

        var result = value;
        foreach (var factor in TrialDivisionFactor(n))
        {
            var prime = (BigInteger)factor.Left;
            result = result / prime * (prime - BigInteger.One);
        }
        return (Integer)result;
    }

    public static Integer DivisorSigma(Integer n, int power = 1)
    {
        var value = BigInteger.Abs((BigInteger)n);
        if (value.IsZero)
            return Integer.Zero;
        if (power < 0)
            throw new ArgumentOutOfRangeException(nameof(power), "Divisor sigma power must be nonnegative.");

        var sum = BigInteger.Zero;
        foreach (var divisor in Divisors(n))
            sum += BigInteger.Pow((BigInteger)divisor, power);
        return (Integer)sum;
    }

    private static BigInteger Mod(BigInteger value, BigInteger modulus)
    {
        var result = value % modulus;
        return result.Sign < 0 ? result + BigInteger.Abs(modulus) : result;
    }
}
