using HPD.Math.Core;

namespace HPD.Math.Numerics;

/// <summary>
/// Status-returning helpers for fixed-width truncated p-adic residues.
/// </summary>
public static class Padic32Kernels
{
    public static AlgebraStatus TryGetModulus<P, N>(out int modulus)
        where P : IPrimeModulus
        where N : IStaticPrecision
    {
        modulus = 0;
        if (P.Value <= 1 || N.Value <= 0)
            return AlgebraStatus.InvalidInput;

        var result = 1L;
        for (var i = 0; i < N.Value; i++)
        {
            result *= P.Value;
            if (result > int.MaxValue)
                return AlgebraStatus.Overflow;
        }

        modulus = (int)result;
        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryCreate<P, N>(int value, out Padic32<P, N> result)
        where P : IPrimeModulus
        where N : IStaticPrecision
    {
        var status = TryReduce<P, N>(value, out var reduced);
        result = status == AlgebraStatus.Ok ? new Padic32<P, N>(reduced, true) : Padic32<P, N>.Zero;
        return status;
    }

    public static AlgebraStatus TryReduce<P, N>(int value, out int reduced)
        where P : IPrimeModulus
        where N : IStaticPrecision
    {
        var status = TryGetModulus<P, N>(out var modulus);
        if (status != AlgebraStatus.Ok)
        {
            reduced = 0;
            return status;
        }

        reduced = value % modulus;
        if (reduced < 0)
            reduced += modulus;
        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryValuation<P, N>(int value, out int valuation)
        where P : IPrimeModulus
        where N : IStaticPrecision
    {
        if (P.Value <= 1 || N.Value <= 0)
        {
            valuation = 0;
            return AlgebraStatus.InvalidInput;
        }

        if (value == 0)
        {
            valuation = N.Value;
            return AlgebraStatus.Ok;
        }

        var count = 0;
        var current = value;
        while (count < N.Value && current % P.Value == 0)
        {
            current /= P.Value;
            count++;
        }

        valuation = count;
        return AlgebraStatus.Ok;
    }
}
