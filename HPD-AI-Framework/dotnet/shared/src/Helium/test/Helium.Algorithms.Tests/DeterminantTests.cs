using Helium.Primitives;
using Helium.Algebra;
using Helium.Algorithms;

namespace Helium.Algorithms.Tests;

public class DeterminantTests
{
    [Fact]
    public void DetOfIdentity_IsOne()
    {
        var I = Matrix<Integer>.Identity(3);
        Assert.Equal(Integer.One, Determinant.Compute(I));
    }

    [Fact]
    public void DetOfScalarTimesMatrix()
    {
        // det(c * A) == c^n * det(A) for n x n matrix
        var A = Matrix<Integer>.FromArray(2, 2, [(Integer)1, (Integer)2, (Integer)3, (Integer)4]);
        Integer c = 3;
        var cA = c * A;
        var detA = Determinant.Compute(A);
        var detcA = Determinant.Compute(cA);
        // c^n * det(A) = 9 * (-2) = -18
        Assert.Equal(Integer.Pow(c, 2) * detA, detcA);
    }

    [Fact]
    public void DetOfProduct_IsProductOfDets()
    {
        var A = Matrix<Integer>.FromArray(2, 2, [(Integer)1, (Integer)2, (Integer)3, (Integer)4]);
        var B = Matrix<Integer>.FromArray(2, 2, [(Integer)5, (Integer)6, (Integer)7, (Integer)8]);
        Assert.Equal(
            Determinant.Compute(A) * Determinant.Compute(B),
            Determinant.Compute(A * B));
    }

    [Fact]
    public void DetOfTranspose_EqualsDet()
    {
        var A = Matrix<Integer>.FromArray(3, 3, [
            (Integer)1, (Integer)2, (Integer)3,
            (Integer)4, (Integer)5, (Integer)6,
            (Integer)7, (Integer)8, (Integer)10]);
        Assert.Equal(Determinant.Compute(A), Determinant.Compute(A.Transpose()));
    }

    [Fact]
    public void DetOfTriangular_IsProductOfDiagonal()
    {
        // Upper triangular matrix.
        var T = Matrix<Integer>.FromArray(3, 3, [
            (Integer)2, (Integer)3, (Integer)4,
            (Integer)0, (Integer)5, (Integer)6,
            (Integer)0, (Integer)0, (Integer)7]);
        Assert.Equal((Integer)(2 * 5 * 7), Determinant.Compute(T));
    }
}
