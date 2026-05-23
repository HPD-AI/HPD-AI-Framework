using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algebra.Tests;

public class FiniteFieldTests
{
    private static ZModElement F(int value, int p) => ZMod.Create((Integer)value, (Integer)p);

    private static SparsePolynomial<ZModElement> P(int characteristic, params int[] coeffs)
    {
        var values = coeffs.Select(c => F(c, characteristic)).ToArray();
        return SparsePolynomial<ZModElement>.FromCoeffs(values);
    }

    [Fact]
    public void GF2Squared_GeneratorSatisfiesDefiningPolynomial()
    {
        // GF(2^2) = F_2[a] / (a^2 + a + 1), so a^2 = a + 1.
        var field = FiniteField.CreatePrimePower((Integer)2, P(2, 1, 1, 1), "a");
        var a = FiniteFieldElement.Generator(field);
        var one = FiniteFieldElement.Create(P(2, 1), field);

        Assert.Equal(a + one, a * a);
        Assert.Equal(one, a * (a + one));
    }

    [Fact]
    public void GF3Squared_InvertGenerator()
    {
        // GF(3^2) = F_3[b] / (b^2 + 1), so b^2 = 2 and b^-1 = 2b.
        var field = FiniteField.CreatePrimePower((Integer)3, P(3, 1, 0, 1), "b");
        var b = FiniteFieldElement.Generator(field);
        var twoB = FiniteFieldElement.Create(P(3, 0, 2), field);

        Assert.Equal(twoB, FiniteFieldElement.Invert(b));
        Assert.Equal(FiniteFieldElement.Create(P(3, 1), field), b * twoB);
    }

    [Fact]
    public void FiniteField_CrossFieldArithmetic_Throws()
    {
        var f4 = FiniteField.CreatePrimePower((Integer)2, P(2, 1, 1, 1), "a");
        var f9 = FiniteField.CreatePrimePower((Integer)3, P(3, 1, 0, 1), "b");

        var a = FiniteFieldElement.Generator(f4);
        var b = FiniteFieldElement.Generator(f9);

        Assert.Throws<InvalidOperationException>(() => a + b);
        Assert.Throws<InvalidOperationException>(() => a * b);
    }
}
