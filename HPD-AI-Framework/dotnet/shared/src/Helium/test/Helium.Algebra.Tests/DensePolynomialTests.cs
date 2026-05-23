using Helium.Algebra;
using Helium.Primitives;

namespace Helium.Algebra.Tests;

public class DensePolynomialTests
{
    [Fact]
    public void FromCoeffs_TrimsTrailingZeros()
    {
        var p = DensePolynomial<Integer>.FromCoeffs((Integer)1, (Integer)2, Integer.Zero, Integer.Zero);

        Assert.False(p.IsZero);
        Assert.Equal(1, p.Degree);
        Assert.Equal((Integer)1, p[0]);
        Assert.Equal((Integer)2, p[1]);
        Assert.Equal(Integer.Zero, p[20]);
        Assert.Equal(2, p.Coefficients.Length);
    }

    [Fact]
    public void Zero_HasDegreeMinusOne()
    {
        var p = DensePolynomial<Integer>.FromCoeffs(Integer.Zero, Integer.Zero);

        Assert.True(p.IsZero);
        Assert.Equal(-1, p.Degree);
        Assert.Equal(0, p.Coefficients.Length);
    }

    [Fact]
    public void Addition_IsExact()
    {
        var a = DensePolynomial<Integer>.FromCoeffs((Integer)1, (Integer)2, (Integer)3);
        var b = DensePolynomial<Integer>.FromCoeffs((Integer)4, (Integer)5);

        var sum = a + b;

        Assert.Equal(DensePolynomial<Integer>.FromCoeffs((Integer)5, (Integer)7, (Integer)3), sum);
    }

    [Fact]
    public void AdditiveInverse_NormalizesToZero()
    {
        var p = DensePolynomial<Integer>.FromCoeffs((Integer)1, (Integer)(-3), (Integer)2);

        Assert.Equal(DensePolynomial<Integer>.Zero, p + (-p));
    }

    [Fact]
    public void Multiplication_UsesConvolution()
    {
        var onePlusX = DensePolynomial<Integer>.FromCoeffs(Integer.One, Integer.One);
        var oneMinusX = DensePolynomial<Integer>.FromCoeffs(Integer.One, (Integer)(-1));

        var product = onePlusX * oneMinusX;

        Assert.Equal(DensePolynomial<Integer>.FromCoeffs(Integer.One, Integer.Zero, (Integer)(-1)), product);
    }

    [Fact]
    public void SparseRoundTrip_PreservesValue()
    {
        var sparse = SparsePolynomial<Integer>.Monomial(5, (Integer)9) + SparsePolynomial<Integer>.C((Integer)2);
        var dense = DensePolynomial<Integer>.FromSparse(sparse);

        Assert.Equal(5, dense.Degree);
        Assert.Equal(sparse, dense.ToSparse());
    }

    [Fact]
    public void RingAxioms_HoldForSampleValues()
    {
        var a = DensePolynomial<Integer>.FromCoeffs((Integer)1, (Integer)2);
        var b = DensePolynomial<Integer>.FromCoeffs((Integer)3, (Integer)4);
        var c = DensePolynomial<Integer>.FromCoeffs((Integer)5, (Integer)6);

        Assert.Equal(a, a + DensePolynomial<Integer>.Zero);
        Assert.Equal(a, DensePolynomial<Integer>.Zero + a);
        Assert.Equal(a, a * DensePolynomial<Integer>.One);
        Assert.Equal(a, DensePolynomial<Integer>.One * a);
        Assert.Equal((a + b) + c, a + (b + c));
        Assert.Equal(a + b, b + a);
        Assert.Equal(DensePolynomial<Integer>.Zero, a + (-a));
        Assert.Equal((a * b) * c, a * (b * c));
        Assert.Equal(a * b, b * a);
        Assert.Equal(a * (b + c), a * b + a * c);
        Assert.Equal((a + b) * c, a * c + b * c);
    }
}
