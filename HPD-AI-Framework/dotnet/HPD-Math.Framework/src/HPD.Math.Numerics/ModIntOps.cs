using HPD.Math.Core;

namespace HPD.Math.Numerics;

/// <summary>
/// Field, order, and finite-enumeration operations for <see cref="ModInt{P}"/>.
/// </summary>
public readonly struct ModIntOps<P> :
    IFieldOps<ModInt<P>>,
    IIntegerEmbeddingOps<ModInt<P>>,
    ITotalOrderOps<ModInt<P>>,
    IFiniteEnumerationOps<ModInt<P>>
    where P : IPrimeModulus
{
    public ModInt<P> Zero => ModInt<P>.Zero;

    public ModInt<P> One => ModInt<P>.One;

    public int Cardinality => P.Value;

    public bool Eq(in ModInt<P> left, in ModInt<P> right) => left.Value == right.Value;

    public bool LessEqual(in ModInt<P> left, in ModInt<P> right) => left.Value <= right.Value;

    public Ordering Compare(in ModInt<P> left, in ModInt<P> right) =>
        left.Value < right.Value ? Ordering.Less :
        left.Value > right.Value ? Ordering.Greater :
        Ordering.Equal;

    public void Add(ref ModInt<P> destination, in ModInt<P> left, in ModInt<P> right) =>
        destination = new ModInt<P>(ModInt<P>.Mod((long)left.Value + right.Value, P.Value));

    public void Sub(ref ModInt<P> destination, in ModInt<P> left, in ModInt<P> right) =>
        destination = new ModInt<P>(ModInt<P>.Mod((long)left.Value - right.Value, P.Value));

    public void Mul(ref ModInt<P> destination, in ModInt<P> left, in ModInt<P> right) =>
        destination = new ModInt<P>(ModInt<P>.Mod((long)left.Value * right.Value, P.Value));

    public void Neg(ref ModInt<P> destination, in ModInt<P> value) =>
        destination = value.Value == 0 ? Zero : new ModInt<P>(P.Value - value.Value);

    public AlgebraStatus TryInvert(ref ModInt<P> destination, in ModInt<P> value)
    {
        if (value.Value == 0)
        {
            destination = Zero;
            return AlgebraStatus.DivisionByZero;
        }

        var status = TryExtendedGcd(value.Value, P.Value, out var gcd, out var inverse, out _);
        if (status != AlgebraStatus.Ok)
            return status;

        if (gcd != 1)
        {
            destination = Zero;
            return AlgebraStatus.NonInvertible;
        }

        destination = new ModInt<P>(inverse);
        return AlgebraStatus.Ok;
    }

    public AlgebraStatus TryGetElement(int index, out ModInt<P> value)
    {
        if (index < 0 || index >= P.Value)
        {
            value = Zero;
            return AlgebraStatus.InvalidInput;
        }

        value = new ModInt<P>(index);
        return AlgebraStatus.Ok;
    }

    public AlgebraStatus TryFromInt(int value, out ModInt<P> result)
    {
        result = new ModInt<P>(value);
        return AlgebraStatus.Ok;
    }

    public AlgebraStatus TryFill(Span<ModInt<P>> destination)
    {
        if (destination.Length < Cardinality)
            return AlgebraStatus.InsufficientDestination;

        for (var i = 0; i < Cardinality; i++)
            destination[i] = new ModInt<P>(i);

        return AlgebraStatus.Ok;
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
