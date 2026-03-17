using Helium.Primitives;
using Helium.Algebra;
using Helium.Algorithms;

namespace Helium.Algorithms.Tests;

public class HermiteNormalFormTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static Matrix<Integer> M(int rows, int cols, params int[] entries)
    {
        var data = entries.Select(x => (Integer)x).ToArray();
        return Matrix<Integer>.FromArray(rows, cols, data);
    }

    private static void AssertHnf(Matrix<Integer> H, Matrix<Integer> expected)
    {
        Assert.Equal(expected.Rows, H.Rows);
        Assert.Equal(expected.Cols, H.Cols);
        for (int i = 0; i < H.Rows; i++)
            for (int j = 0; j < H.Cols; j++)
                Assert.Equal(expected[i, j], H[i, j]);
    }

    private static bool IsUpperTriangular(Matrix<Integer> H)
    {
        for (int i = 0; i < H.Rows; i++)
            for (int j = 0; j < i && j < H.Cols; j++)
                if (!H[i, j].IsZero) return false;
        return true;
    }

    private static bool HasPositiveDiagonal(Matrix<Integer> H)
    {
        int k = Math.Min(H.Rows, H.Cols);
        for (int i = 0; i < k; i++)
            if (!H[i, i].IsZero && H[i, i].Sign <= 0) return false;
        return true;
    }

    // HNF condition: for each pivot column j with diagonal d = H[j,j],
    // all entries above the pivot in that column satisfy 0 ≤ H[i,j] < d.
    private static bool AboveDiagonalReduced(Matrix<Integer> H)
    {
        int k = Math.Min(H.Rows, H.Cols);
        for (int j = 0; j < k; j++)
        {
            var diag = H[j, j];
            if (diag.IsZero) continue;
            for (int i = 0; i < j; i++)
            {
                if (H[i, j].Sign < 0 || H[i, j] >= diag) return false;
            }
        }
        return true;
    }

    // -------------------------------------------------------------------------
    // Identity
    // -------------------------------------------------------------------------

    [Fact]
    public void Identity2x2_IsFixed()
    {
        var A = Matrix<Integer>.Identity(2);
        var H = HermiteNormalForm.Compute(A);
        AssertHnf(H, Matrix<Integer>.Identity(2));
    }

    [Fact]
    public void Identity3x3_IsFixed()
    {
        var A = Matrix<Integer>.Identity(3);
        var H = HermiteNormalForm.Compute(A);
        AssertHnf(H, Matrix<Integer>.Identity(3));
    }

    // -------------------------------------------------------------------------
    // Single column
    // -------------------------------------------------------------------------

    [Fact]
    public void SingleColumn_PositiveEntries()
    {
        // Column [4, 6]^T → HNF should have gcd(4,6)=2 at top, 0 below
        var A = M(2, 1, 4, 6);
        var H = HermiteNormalForm.Compute(A);
        Assert.True(IsUpperTriangular(H));
        Assert.True(HasPositiveDiagonal(H));
    }

    [Fact]
    public void SingleColumn_NegativeEntry()
    {
        var A = M(2, 1, -3, 6);
        var H = HermiteNormalForm.Compute(A);
        Assert.True(H[0, 0].Sign > 0 || H[0, 0].IsZero);
    }

    // -------------------------------------------------------------------------
    // 2×2 examples
    // -------------------------------------------------------------------------

    [Fact]
    public void TwoByTwo_AlreadyHnf()
    {
        // [[2, 1], [0, 3]] is already in HNF (upper tri, pos diag, 0 ≤ h01 < h11)
        var A = M(2, 2, 2, 1, 0, 3);
        var H = HermiteNormalForm.Compute(A);
        AssertHnf(H, A);
    }

    [Fact]
    public void TwoByTwo_Simple()
    {
        // [[0, 2], [3, 0]] — needs column swap + GCD reduction
        var A = M(2, 2, 0, 2, 3, 0);
        var H = HermiteNormalForm.Compute(A);
        Assert.True(IsUpperTriangular(H));
        Assert.True(HasPositiveDiagonal(H));
        Assert.True(AboveDiagonalReduced(H));
    }

    [Fact]
    public void TwoByTwo_ColsSwapped()
    {
        // [[3, 0], [0, 2]] — diagonal, columns reversed relative to HNF order
        var A = M(2, 2, 3, 0, 0, 2);
        var H = HermiteNormalForm.Compute(A);
        Assert.True(IsUpperTriangular(H));
        Assert.True(HasPositiveDiagonal(H));
    }

    [Fact]
    public void TwoByTwo_WithOffDiagonalReduction()
    {
        // [[6, 10], [0, 4]]: h01 = 10 should be reduced mod 4 to 2
        var A = M(2, 2, 6, 10, 0, 4);
        var H = HermiteNormalForm.Compute(A);
        Assert.True(IsUpperTriangular(H));
        Assert.True(HasPositiveDiagonal(H));
        Assert.True(AboveDiagonalReduced(H));
    }

    // -------------------------------------------------------------------------
    // 3×3 examples
    // -------------------------------------------------------------------------

    [Fact]
    public void ThreeByThree_Identity()
    {
        var A = Matrix<Integer>.Identity(3);
        var H = HermiteNormalForm.Compute(A);
        AssertHnf(H, Matrix<Integer>.Identity(3));
    }

    [Fact]
    public void ThreeByThree_UpperTriangular()
    {
        // Already upper triangular
        var A = M(3, 3,
            2, 3, 1,
            0, 5, 2,
            0, 0, 7);
        var H = HermiteNormalForm.Compute(A);
        Assert.True(IsUpperTriangular(H));
        Assert.True(HasPositiveDiagonal(H));
        Assert.True(AboveDiagonalReduced(H));
    }

    [Fact]
    public void ThreeByThree_NeedsGcdElim()
    {
        var A = M(3, 3,
            2, 4, 6,
            3, 6, 9,
            1, 2, 3);
        var H = HermiteNormalForm.Compute(A);
        Assert.True(IsUpperTriangular(H));
        Assert.True(HasPositiveDiagonal(H));
        Assert.True(AboveDiagonalReduced(H));
    }

    // -------------------------------------------------------------------------
    // Modular overload
    // -------------------------------------------------------------------------

    [Fact]
    public void ModularOverload_MatchesGeneral_2x2()
    {
        var A = M(2, 2, 6, 10, 0, 4);
        var Hgen = HermiteNormalForm.Compute(A);
        var Hmod = HermiteNormalForm.Compute(A, (Integer)100);
        // Both should satisfy HNF structural properties
        Assert.True(IsUpperTriangular(Hmod));
        Assert.True(HasPositiveDiagonal(Hmod));
        Assert.True(AboveDiagonalReduced(Hmod));
        // Diagonal should agree
        Assert.Equal(Hgen[0, 0], Hmod[0, 0]);
        Assert.Equal(Hgen[1, 1], Hmod[1, 1]);
    }

    [Fact]
    public void ModularOverload_Identity()
    {
        var A = Matrix<Integer>.Identity(3);
        var H = HermiteNormalForm.Compute(A, (Integer)1000);
        Assert.True(IsUpperTriangular(H));
        Assert.True(HasPositiveDiagonal(H));
    }

    // -------------------------------------------------------------------------
    // Zero matrix
    // -------------------------------------------------------------------------

    [Fact]
    public void ZeroMatrix_RemainsZero()
    {
        var A = Matrix<Integer>.Zero(3, 3);
        var H = HermiteNormalForm.Compute(A);
        Assert.True(IsUpperTriangular(H));
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                Assert.Equal(Integer.Zero, H[i, j]);
    }

    // -------------------------------------------------------------------------
    // Rectangular
    // -------------------------------------------------------------------------

    [Fact]
    public void Rectangular_WiderThanTall()
    {
        // 2×3 matrix
        var A = M(2, 3,
            2, 3, 1,
            4, 1, 5);
        var H = HermiteNormalForm.Compute(A);
        Assert.True(IsUpperTriangular(H));
        Assert.True(HasPositiveDiagonal(H));
        Assert.True(AboveDiagonalReduced(H));
    }

    [Fact]
    public void Rectangular_TallerThanWide()
    {
        // 3×2 matrix
        var A = M(3, 2,
            1, 2,
            3, 4,
            5, 6);
        var H = HermiteNormalForm.Compute(A);
        Assert.True(IsUpperTriangular(H));
        Assert.True(HasPositiveDiagonal(H));
    }

    // -------------------------------------------------------------------------
    // Negative entries
    // -------------------------------------------------------------------------

    [Fact]
    public void NegativeEntries_DiagonalPositive()
    {
        var A = M(2, 2, -2, -3, -4, -5);
        var H = HermiteNormalForm.Compute(A);
        Assert.True(IsUpperTriangular(H));
        Assert.True(HasPositiveDiagonal(H));
        Assert.True(AboveDiagonalReduced(H));
    }

    // -------------------------------------------------------------------------
    // Idempotency: HNF of HNF is itself
    // -------------------------------------------------------------------------

    [Fact]
    public void Idempotent_2x2()
    {
        var A = M(2, 2, 3, 7, 1, 5);
        var H1 = HermiteNormalForm.Compute(A);
        var H2 = HermiteNormalForm.Compute(H1);
        AssertHnf(H2, H1);
    }

    [Fact]
    public void Idempotent_3x3()
    {
        var A = M(3, 3,
            1, 2, 3,
            4, 5, 6,
            7, 8, 10);
        var H1 = HermiteNormalForm.Compute(A);
        var H2 = HermiteNormalForm.Compute(H1);
        AssertHnf(H2, H1);
    }
}
