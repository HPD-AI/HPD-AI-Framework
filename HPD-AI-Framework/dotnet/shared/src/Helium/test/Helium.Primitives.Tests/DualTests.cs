using Helium.Primitives;

namespace Helium.Primitives.Tests;

public class DualTests
{
    private static Rational R(int n) => (Rational)n;
    private static Rational R(int num, int den) => Rational.Create((Integer)num, (Integer)den);

    [Fact]
    public void Seed_HasUnitTangent()
    {
        var x = Dual<Rational>.Seed(R(5));
        Assert.Equal(R(5), x.Primal);
        Assert.Equal(R(1), x.Tangent);
    }

    [Fact]
    public void Constant_HasZeroTangent()
    {
        var x = Dual<Rational>.Constant(R(5));
        Assert.Equal(R(5), x.Primal);
        Assert.Equal(R(0), x.Tangent);
    }

    [Fact]
    public void Multiplication_UsesProductRule()
    {
        var x = Dual<Rational>.Seed(R(3));
        var y = x * x;
        Assert.Equal(R(9), y.Primal);
        Assert.Equal(R(6), y.Tangent);
    }

    [Fact]
    public void Invert_UsesReciprocalDerivative()
    {
        var x = Dual<Rational>.Seed(R(2));
        var y = Dual<Rational>.Invert(x);
        Assert.Equal(R(1, 2), y.Primal);
        Assert.Equal(R(-1, 4), y.Tangent);
    }

    [Fact]
    public void Division_ComposesWithInvert()
    {
        var x = Dual<Rational>.Seed(R(6));
        var y = x / Dual<Rational>.Constant(R(3));
        Assert.Equal(R(2), y.Primal);
        Assert.Equal(R(1, 3), y.Tangent);
    }
}
