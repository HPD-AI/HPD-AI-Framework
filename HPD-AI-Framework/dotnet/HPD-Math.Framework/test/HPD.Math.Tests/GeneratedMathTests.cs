using HPD.Math.Core;

namespace HPD.Math.Tests;

[Dimension(3)]
public readonly partial struct Dim3;

[Dimension(2)]
public readonly partial struct Dim2;

[PrimeModulus(17)]
public readonly partial struct P17;

[Precision(8)]
public readonly partial struct N8;

public sealed class GeneratedMathTests
{
    [Fact]
    public void Generator_EmitsStaticWitnesses()
    {
        Assert.Equal(3, Dim3.Value);
        Assert.Equal(2, Dim2.Value);
        Assert.Equal(17, P17.Value);
        Assert.Equal(8, N8.Value);
    }
}

public readonly struct GeneratedMod7FieldOps : IFieldOps<int>
{
    public int Zero => 0;
    public int One => 1;

    public bool Eq(in int left, in int right) => Mod(left) == Mod(right);

    public void Add(ref int destination, in int left, in int right) =>
        destination = Mod(left + right);

    public void Sub(ref int destination, in int left, in int right) =>
        destination = Mod(left - right);

    public void Mul(ref int destination, in int left, in int right) =>
        destination = Mod(left * right);

    public void Neg(ref int destination, in int value) =>
        destination = Mod(-value);

    public AlgebraStatus TryInvert(ref int destination, in int value)
    {
        var normalized = Mod(value);
        if (normalized == 0)
            return AlgebraStatus.DivisionByZero;

        for (var i = 1; i < 7; i++)
        {
            if (Mod(normalized * i) != 1)
                continue;

            destination = i;
            return AlgebraStatus.Ok;
        }

        return AlgebraStatus.NonInvertible;
    }

    private static int Mod(int value)
    {
        var result = value % 7;
        return result < 0 ? result + 7 : result;
    }
}
