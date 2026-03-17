using Helium.Primitives;

namespace Helium.Primitives.Tests;

public class RationalEuclideanDomainTests
{
    private static Rational R(int num, int den) =>
        Rational.Create((Integer)num, (Integer)den);

    // =========================================================================
    // DivMod — remainder is always zero for a field
    // =========================================================================

    [Fact]
    public void DivMod_RemainderIsAlwaysZero()
    {
        var (_, r1) = Rational.DivMod(R(3, 4), R(1, 2));
        var (_, r2) = Rational.DivMod(R(1, 1), (Rational)7);
        var (_, r3) = Rational.DivMod(R(-3, 1), R(2, 1));
        Assert.Equal(Rational.Zero, r1);
        Assert.Equal(Rational.Zero, r2);
        Assert.Equal(Rational.Zero, r3);
    }

    [Fact]
    public void DivMod_QuotientEqualsADividedByB()
    {
        var (q, _) = Rational.DivMod(R(3, 4), R(1, 2));
        // 3/4 / 1/2 = 3/2
        Assert.Equal(R(3, 2), q);
    }

    [Fact]
    public void DivMod_QuotientSatisfies_QTimesB_EqualsA()
    {
        var a = R(7, 3);
        var b = R(5, 11);
        var (q, r) = Rational.DivMod(a, b);
        Assert.Equal(a, q * b + r);
    }

    [Fact]
    public void DivMod_AIsZero_QuotientAndRemainderAreZero()
    {
        var (q, r) = Rational.DivMod(Rational.Zero, R(3, 4));
        Assert.Equal(Rational.Zero, q);
        Assert.Equal(Rational.Zero, r);
    }

    [Fact]
    public void DivMod_NegativeNumerator()
    {
        var (q, _) = Rational.DivMod(R(-3, 1), R(2, 1));
        Assert.Equal(R(-3, 2), q);
    }

    // =========================================================================
    // Gcd
    // =========================================================================

    [Fact]
    public void Gcd_BothNonzero_IsOne()
    {
        Assert.Equal(Rational.One, Rational.Gcd(R(3, 4), R(2, 5)));
        Assert.Equal(Rational.One, Rational.Gcd(R(1, 1), R(7, 3)));
    }

    [Fact]
    public void Gcd_LeftZero_IsRight()
    {
        Assert.Equal(R(3, 4), Rational.Gcd(Rational.Zero, R(3, 4)));
    }

    [Fact]
    public void Gcd_RightZero_IsLeft()
    {
        Assert.Equal(R(1, 2), Rational.Gcd(R(1, 2), Rational.Zero));
    }

    [Fact]
    public void Gcd_BothZero_IsZero()
    {
        Assert.Equal(Rational.Zero, Rational.Gcd(Rational.Zero, Rational.Zero));
    }

    [Fact]
    public void Gcd_Divides_BothInputs()
    {
        // In a field, Gcd(a,b)=1 divides everything.
        var a = R(5, 7);
        var b = R(3, 11);
        var g = Rational.Gcd(a, b);
        // g divides a means a/g has zero remainder.
        var (_, ra) = Rational.DivMod(a, g);
        var (_, rb) = Rational.DivMod(b, g);
        Assert.Equal(Rational.Zero, ra);
        Assert.Equal(Rational.Zero, rb);
    }

    // =========================================================================
    // Lcm
    // =========================================================================

    [Fact]
    public void Lcm_BothNonzero_IsOne()
    {
        Assert.Equal(Rational.One, Rational.Lcm(R(3, 4), R(2, 5)));
    }

    [Fact]
    public void Lcm_LeftZero_IsZero()
    {
        Assert.Equal(Rational.Zero, Rational.Lcm(Rational.Zero, R(1, 2)));
    }

    [Fact]
    public void Lcm_RightZero_IsZero()
    {
        Assert.Equal(Rational.Zero, Rational.Lcm(R(1, 2), Rational.Zero));
    }

    [Fact]
    public void Lcm_BothZero_IsZero()
    {
        Assert.Equal(Rational.Zero, Rational.Lcm(Rational.Zero, Rational.Zero));
    }

    [Fact]
    public void Lcm_IsMultipleOfBothInputs()
    {
        // Lcm(a,b) = 1, which is a multiple of a and b in a field (a divides 1 means 1/a exists).
        // Equivalently, DivMod(lcm, a) has remainder 0.
        var a = R(3, 7);
        var b = R(11, 5);
        var lcm = Rational.Lcm(a, b);
        var (_, ra) = Rational.DivMod(lcm, a);
        var (_, rb) = Rational.DivMod(lcm, b);
        Assert.Equal(Rational.Zero, ra);
        Assert.Equal(Rational.Zero, rb);
    }
}
