using Helium.Primitives;
using Helium.Algebra;

namespace Helium.Algebra.Tests;

public class MatrixTests
{
    // --- Construction ---

    [Fact]
    public void IdentityMatrix()
    {
        var I = Matrix<Integer>.Identity(3);
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                Assert.Equal(i == j ? Integer.One : Integer.Zero, I[i, j]);
    }

    [Fact]
    public void ZeroMatrix()
    {
        var Z = Matrix<Integer>.Zero(2, 3);
        Assert.Equal(2, Z.Rows);
        Assert.Equal(3, Z.Cols);
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 3; j++)
                Assert.Equal(Integer.Zero, Z[i, j]);
    }

    // --- Arithmetic ---

    [Fact]
    public void Addition()
    {
        var A = Matrix<Integer>.FromArray(2, 2, [(Integer)1, (Integer)2, (Integer)3, (Integer)4]);
        var B = Matrix<Integer>.FromArray(2, 2, [(Integer)5, (Integer)6, (Integer)7, (Integer)8]);
        var C = A + B;
        Assert.Equal((Integer)6, C[0, 0]);
        Assert.Equal((Integer)8, C[0, 1]);
        Assert.Equal((Integer)10, C[1, 0]);
        Assert.Equal((Integer)12, C[1, 1]);
    }

    [Fact]
    public void IdentityMultiplication()
    {
        var A = Matrix<Integer>.FromArray(2, 2, [(Integer)1, (Integer)2, (Integer)3, (Integer)4]);
        var I = Matrix<Integer>.Identity(2);
        Assert.Equal(A, A * I);
        Assert.Equal(A, I * A);
    }

    [Fact]
    public void MatrixMultiplication()
    {
        // [[1,2],[3,4]] * [[5,6],[7,8]] = [[19,22],[43,50]]
        var A = Matrix<Integer>.FromArray(2, 2, [(Integer)1, (Integer)2, (Integer)3, (Integer)4]);
        var B = Matrix<Integer>.FromArray(2, 2, [(Integer)5, (Integer)6, (Integer)7, (Integer)8]);
        var C = A * B;
        Assert.Equal((Integer)19, C[0, 0]);
        Assert.Equal((Integer)22, C[0, 1]);
        Assert.Equal((Integer)43, C[1, 0]);
        Assert.Equal((Integer)50, C[1, 1]);
    }

    [Fact]
    public void Associativity()
    {
        var A = Matrix<Integer>.FromArray(2, 2, [(Integer)1, (Integer)2, (Integer)3, (Integer)4]);
        var B = Matrix<Integer>.FromArray(2, 2, [(Integer)5, (Integer)6, (Integer)7, (Integer)8]);
        var C = Matrix<Integer>.FromArray(2, 2, [(Integer)9, (Integer)10, (Integer)11, (Integer)12]);
        Assert.Equal((A * B) * C, A * (B * C));
    }

    [Fact]
    public void Distributivity()
    {
        var A = Matrix<Integer>.FromArray(2, 2, [(Integer)1, (Integer)2, (Integer)3, (Integer)4]);
        var B = Matrix<Integer>.FromArray(2, 2, [(Integer)5, (Integer)6, (Integer)7, (Integer)8]);
        var C = Matrix<Integer>.FromArray(2, 2, [(Integer)9, (Integer)10, (Integer)11, (Integer)12]);
        Assert.Equal(A * (B + C), A * B + A * C);
    }

    [Fact]
    public void ScalarMultiplication()
    {
        var A = Matrix<Integer>.FromArray(2, 2, [(Integer)1, (Integer)2, (Integer)3, (Integer)4]);
        var result = (Integer)3 * A;
        Assert.Equal((Integer)3, result[0, 0]);
        Assert.Equal((Integer)6, result[0, 1]);
        Assert.Equal((Integer)9, result[1, 0]);
        Assert.Equal((Integer)12, result[1, 1]);
    }

    // --- Transpose ---

    [Fact]
    public void TransposeTransposeIsIdentity()
    {
        var A = Matrix<Integer>.FromArray(2, 3, [(Integer)1, (Integer)2, (Integer)3, (Integer)4, (Integer)5, (Integer)6]);
        Assert.Equal(A, A.Transpose().Transpose());
    }

    [Fact]
    public void TransposeOfIdentity()
    {
        var I = Matrix<Integer>.Identity(3);
        Assert.Equal(I, I.Transpose());
    }

    [Fact]
    public void TransposeOfProduct()
    {
        var A = Matrix<Integer>.FromArray(2, 2, [(Integer)1, (Integer)2, (Integer)3, (Integer)4]);
        var B = Matrix<Integer>.FromArray(2, 2, [(Integer)5, (Integer)6, (Integer)7, (Integer)8]);
        Assert.Equal((A * B).Transpose(), B.Transpose() * A.Transpose());
    }

    // --- Vector operations ---

    [Fact]
    public void MatrixVectorProduct()
    {
        var A = Matrix<Integer>.FromArray(2, 2, [(Integer)1, (Integer)2, (Integer)3, (Integer)4]);
        var v = Vector<Integer>.FromArray((Integer)5, (Integer)6);
        var result = A * v;
        Assert.Equal((Integer)17, result[0]); // 1*5 + 2*6
        Assert.Equal((Integer)39, result[1]); // 3*5 + 4*6
    }

    [Fact]
    public void DotProduct()
    {
        var v = Vector<Integer>.FromArray((Integer)1, (Integer)2, (Integer)3);
        var w = Vector<Integer>.FromArray((Integer)4, (Integer)5, (Integer)6);
        Assert.Equal((Integer)32, Vector<Integer>.Dot(v, w)); // 4+10+18
    }

    [Fact]
    public void VectorAddition()
    {
        var v = Vector<Integer>.FromArray((Integer)1, (Integer)2);
        var w = Vector<Integer>.FromArray((Integer)3, (Integer)4);
        var sum = v + w;
        Assert.Equal((Integer)4, sum[0]);
        Assert.Equal((Integer)6, sum[1]);
    }
}
