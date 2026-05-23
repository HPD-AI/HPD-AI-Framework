using Helium.Algebra;
using Helium.Primitives;

namespace Helium.Algebra.Tests;

public class QuaternionTests
{
    private static Rational R(int n) => (Rational)n;
    private static Quaternion<Rational> Q(int real, int i, int j, int k) =>
        new(R(real), R(i), R(j), R(k));

    [Fact]
    public void HamiltonBasisRules()
    {
        var minusOne = -Quaternion<Rational>.One;

        Assert.Equal(minusOne, Quaternion<Rational>.BasisI * Quaternion<Rational>.BasisI);
        Assert.Equal(minusOne, Quaternion<Rational>.BasisJ * Quaternion<Rational>.BasisJ);
        Assert.Equal(minusOne, Quaternion<Rational>.BasisK * Quaternion<Rational>.BasisK);
        Assert.Equal(Quaternion<Rational>.BasisK, Quaternion<Rational>.BasisI * Quaternion<Rational>.BasisJ);
        Assert.Equal(Quaternion<Rational>.BasisI, Quaternion<Rational>.BasisJ * Quaternion<Rational>.BasisK);
        Assert.Equal(Quaternion<Rational>.BasisJ, Quaternion<Rational>.BasisK * Quaternion<Rational>.BasisI);
    }

    [Fact]
    public void Multiplication_IsNotCommutative()
    {
        Assert.Equal(Quaternion<Rational>.BasisK, Quaternion<Rational>.BasisI * Quaternion<Rational>.BasisJ);
        Assert.Equal(-Quaternion<Rational>.BasisK, Quaternion<Rational>.BasisJ * Quaternion<Rational>.BasisI);
    }

    [Fact]
    public void Invert_NonzeroQuaternion()
    {
        var q = Q(1, 2, 3, 4);
        var inverse = Quaternion<Rational>.Invert(q);

        Assert.Equal(Quaternion<Rational>.One, q * inverse);
        Assert.Equal(Quaternion<Rational>.One, inverse * q);
        Assert.Equal(q, Quaternion<Rational>.Invert(inverse));
    }

    [Fact]
    public void InvertZero_ReturnsZeroByTotalFunctionConvention()
    {
        Assert.Equal(Quaternion<Rational>.Zero, Quaternion<Rational>.Invert(Quaternion<Rational>.Zero));
    }

    [Fact]
    public void Star_IsConjugationInvolution()
    {
        var q = Q(1, 2, -3, 4);

        Assert.Equal(Q(1, -2, 3, -4), Quaternion<Rational>.Star(q));
        Assert.Equal(q, Quaternion<Rational>.Star(Quaternion<Rational>.Star(q)));
    }

    [Fact]
    public void NormSquared_IsMultiplicative()
    {
        var p = Q(1, 2, 3, 4);
        var q = Q(-2, 5, 7, 11);

        Assert.Equal(p.NormSquared() * q.NormSquared(), (p * q).NormSquared());
    }

    [Fact]
    public void FromInt_EmbedsIntoRealComponent()
    {
        var q = Quaternion<Rational>.FromInt(7);

        Assert.Equal(Q(7, 0, 0, 0), q);
    }

    [Fact]
    public void Quaternion_IsDivisionRing_NotField()
    {
        var interfaces = typeof(Quaternion<Rational>).GetInterfaces();

        Assert.Contains(interfaces, type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IDivisionRing<>));
        Assert.DoesNotContain(interfaces, type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IField<>));
    }
}
