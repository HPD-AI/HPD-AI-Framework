using Helium.Primitives;
using Helium.Algebra;
using Helium.Algorithms;

namespace Helium.Algorithms.Tests;

public class CharacteristicPolynomialTests
{
    [Fact]
    public void Degree_EqualsMatrixDimension()
    {
        var A = Matrix<Integer>.FromArray(3, 3, [
            (Integer)1, (Integer)2, (Integer)3,
            (Integer)4, (Integer)5, (Integer)6,
            (Integer)7, (Integer)8, (Integer)10]);
        var cp = CharacteristicPolynomial.Compute(A);
        Assert.Equal(3, cp.Degree);
    }

    [Fact]
    public void LeadingCoefficient_IsOne()
    {
        var A = Matrix<Integer>.FromArray(2, 2, [(Integer)1, (Integer)2, (Integer)3, (Integer)4]);
        var cp = CharacteristicPolynomial.Compute(A);
        Assert.Equal(Integer.One, cp.LeadingCoefficient);
    }

    [Fact]
    public void ConstantTerm_IsSignedDet()
    {
        // c_0 = (-1)^n * det(A)
        var A = Matrix<Integer>.FromArray(2, 2, [(Integer)1, (Integer)2, (Integer)3, (Integer)4]);
        var cp = CharacteristicPolynomial.Compute(A);
        var det = Determinant.Compute(A); // -2
        // (-1)^2 * det = 1 * (-2) = -2
        Assert.Equal(det, cp[0]);
    }

    [Fact]
    public void CayleyHamilton_CharpOfA_IsZero()
    {
        // Evaluate p(A) where p is the characteristic polynomial.
        // p(A) should be the zero matrix (Cayley-Hamilton theorem).
        var A = Matrix<Integer>.FromArray(2, 2, [(Integer)1, (Integer)2, (Integer)3, (Integer)4]);
        var cp = CharacteristicPolynomial.Compute(A);

        // Evaluate p(A) = A^2 + c1*A + c0*I
        int n = A.Rows;
        var result = Matrix<Integer>.Zero(n, n);
        var power = Matrix<Integer>.Identity(n); // A^0

        for (int i = 0; i <= cp.Degree; i++)
        {
            result = result + cp[i] * power;
            if (i < cp.Degree)
                power = power * A;
        }

        Assert.Equal(Matrix<Integer>.Zero(n, n), result);
    }
}
