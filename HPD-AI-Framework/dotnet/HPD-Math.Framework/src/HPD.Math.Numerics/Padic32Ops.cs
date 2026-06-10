using HPD.Math.Core;

namespace HPD.Math.Numerics;

/// <summary>
/// Ring, order, and finite-enumeration operations for truncated p-adic residues modulo p^N.
/// </summary>
public readonly struct Padic32Ops<P, N> :
    IStatusRingOps<Padic32<P, N>>,
    ITotalOrderOps<Padic32<P, N>>,
    IFiniteEnumerationOps<Padic32<P, N>>
    where P : IPrimeModulus
    where N : IStaticPrecision
{
    public Padic32<P, N> Zero => Padic32<P, N>.Zero;

    public Padic32<P, N> One => Padic32<P, N>.One;

    public int Cardinality
    {
        get
        {
            var status = Padic32Kernels.TryGetModulus<P, N>(out var modulus);
            return status == AlgebraStatus.Ok ? modulus : 0;
        }
    }

    public bool Eq(in Padic32<P, N> left, in Padic32<P, N> right) => left.Value == right.Value;

    public bool LessEqual(in Padic32<P, N> left, in Padic32<P, N> right) => left.Value <= right.Value;

    public Ordering Compare(in Padic32<P, N> left, in Padic32<P, N> right) =>
        left.Value < right.Value ? Ordering.Less :
        left.Value > right.Value ? Ordering.Greater :
        Ordering.Equal;

    public AlgebraStatus TryAdd(ref Padic32<P, N> destination, in Padic32<P, N> left, in Padic32<P, N> right)
    {
        var status = Padic32Kernels.TryGetModulus<P, N>(out var modulus);
        if (status != AlgebraStatus.Ok)
        {
            destination = Zero;
            return status;
        }

        destination = new Padic32<P, N>(Mod((long)left.Value + right.Value, modulus), true);
        return AlgebraStatus.Ok;
    }

    public AlgebraStatus TrySub(ref Padic32<P, N> destination, in Padic32<P, N> left, in Padic32<P, N> right)
    {
        var status = Padic32Kernels.TryGetModulus<P, N>(out var modulus);
        if (status != AlgebraStatus.Ok)
        {
            destination = Zero;
            return status;
        }

        destination = new Padic32<P, N>(Mod((long)left.Value - right.Value, modulus), true);
        return AlgebraStatus.Ok;
    }

    public AlgebraStatus TryMul(ref Padic32<P, N> destination, in Padic32<P, N> left, in Padic32<P, N> right)
    {
        var status = Padic32Kernels.TryGetModulus<P, N>(out var modulus);
        if (status != AlgebraStatus.Ok)
        {
            destination = Zero;
            return status;
        }

        destination = new Padic32<P, N>(Mod((long)left.Value * right.Value, modulus), true);
        return AlgebraStatus.Ok;
    }

    public AlgebraStatus TryNeg(ref Padic32<P, N> destination, in Padic32<P, N> value)
    {
        var status = Padic32Kernels.TryGetModulus<P, N>(out var modulus);
        if (status != AlgebraStatus.Ok)
        {
            destination = Zero;
            return status;
        }

        destination = value.Value == 0 ? Zero : new Padic32<P, N>(modulus - value.Value, true);
        return AlgebraStatus.Ok;
    }

    public AlgebraStatus TryInvert(ref Padic32<P, N> destination, in Padic32<P, N> value)
    {
        var status = Padic32Kernels.TryGetModulus<P, N>(out var modulus);
        if (status != AlgebraStatus.Ok)
        {
            destination = Zero;
            return status;
        }

        if (!value.IsUnit)
        {
            destination = Zero;
            return AlgebraStatus.NonInvertible;
        }

        status = TryExtendedGcd(value.Value, modulus, out var gcd, out var inverse, out _);
        if (status != AlgebraStatus.Ok)
        {
            destination = Zero;
            return status;
        }

        if (gcd != 1)
        {
            destination = Zero;
            return AlgebraStatus.NonInvertible;
        }

        destination = new Padic32<P, N>(Mod(inverse, modulus), true);
        return AlgebraStatus.Ok;
    }

    public AlgebraStatus TryGetElement(int index, out Padic32<P, N> value)
    {
        var status = Padic32Kernels.TryGetModulus<P, N>(out var modulus);
        if (status != AlgebraStatus.Ok)
        {
            value = Zero;
            return status;
        }

        if (index < 0 || index >= modulus)
        {
            value = Zero;
            return AlgebraStatus.InvalidInput;
        }

        value = new Padic32<P, N>(index, true);
        return AlgebraStatus.Ok;
    }

    public AlgebraStatus TryFromInt(int value, out Padic32<P, N> result) =>
        Padic32Kernels.TryCreate<P, N>(value, out result);

    public AlgebraStatus TryFill(Span<Padic32<P, N>> destination)
    {
        var status = Padic32Kernels.TryGetModulus<P, N>(out var modulus);
        if (status != AlgebraStatus.Ok)
            return status;

        if (destination.Length < modulus)
            return AlgebraStatus.InsufficientDestination;

        for (var i = 0; i < modulus; i++)
            destination[i] = new Padic32<P, N>(i, true);

        return AlgebraStatus.Ok;
    }

    private static int Mod(long value, int modulus)
    {
        var result = (int)(value % modulus);
        return result < 0 ? result + modulus : result;
    }

    private static AlgebraStatus TryExtendedGcd(int a, int b, out int gcd, out int s, out int t)
    {
        var oldR = a;
        var r = b;
        var oldS = 1;
        var nextS = 0;
        var oldT = 0;
        var nextT = 1;

        while (r != 0)
        {
            var quotient = oldR / r;

            var tempR = oldR - quotient * r;
            oldR = r;
            r = tempR;

            var tempS = oldS - quotient * nextS;
            oldS = nextS;
            nextS = tempS;

            var tempT = oldT - quotient * nextT;
            oldT = nextT;
            nextT = tempT;
        }

        if (oldR < 0)
        {
            oldR = -oldR;
            oldS = -oldS;
            oldT = -oldT;
        }

        gcd = oldR;
        s = oldS;
        t = oldT;
        return AlgebraStatus.Ok;
    }
}
