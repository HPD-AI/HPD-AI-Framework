using Helium.Primitives;
using Helium.Algebra;
using Helium.Algorithms;

namespace Helium.Algorithms.Tests;

public class PolynomialContentTests
{
    [Fact]
    public void ContentOfPrimitive()
    {
        // 2x^2 + 3x + 1 has content 1 (coeffs are coprime)
        var p = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)3, (Integer)2);
        Assert.Equal((Integer)1, PolynomialContent.Compute(p));
    }

    [Fact]
    public void ContentOfNonPrimitive()
    {
        // 6x^2 + 4x + 2 has content 2
        var p = Polynomial<Integer>.FromCoeffs((Integer)2, (Integer)4, (Integer)6);
        Assert.Equal((Integer)2, PolynomialContent.Compute(p));
    }

    [Fact]
    public void ContentOfZero()
    {
        Assert.Equal(Integer.Zero, PolynomialContent.Compute(Polynomial<Integer>.Zero));
    }

    [Fact]
    public void PrimitivePart_DividesOutContent()
    {
        // 6x^2 + 4x + 2 -> primitive part = 3x^2 + 2x + 1
        var p = Polynomial<Integer>.FromCoeffs((Integer)2, (Integer)4, (Integer)6);
        var pp = PolynomialContent.PrimitivePart(p);
        Assert.Equal((Integer)1, pp[0]);
        Assert.Equal((Integer)2, pp[1]);
        Assert.Equal((Integer)3, pp[2]);
    }

    [Fact]
    public void PrimitivePart_OfPrimitive_IsItself()
    {
        var p = Polynomial<Integer>.FromCoeffs((Integer)1, (Integer)3, (Integer)2);
        var pp = PolynomialContent.PrimitivePart(p);
        Assert.Equal(p, pp);
    }

    [Fact]
    public void PrimitivePart_OfZero_IsZero()
    {
        Assert.Equal(Polynomial<Integer>.Zero, PolynomialContent.PrimitivePart(Polynomial<Integer>.Zero));
    }
}
