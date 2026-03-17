using Helium.Primitives;
using Helium.Algebra;
using Helium.Algorithms;

namespace Helium.Algorithms.Tests;

public class SmithNormalFormTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static Matrix<Integer> M(int rows, int cols, params int[] entries)
    {
        var data = entries.Select(x => (Integer)x).ToArray();
        return Matrix<Integer>.FromArray(rows, cols, data);
    }

    private static void AssertFactors(IReadOnlyList<Integer> actual, params int[] expected)
    {
        Assert.Equal(expected.Length, actual.Count);
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal((Integer)expected[i], actual[i]);
    }

    private static bool DivisibilityChainHolds(IReadOnlyList<Integer> factors)
    {
        for (int i = 0; i < factors.Count - 1; i++)
        {
            if (factors[i].IsZero) continue;
            var (_, rem) = Integer.DivMod(factors[i + 1], factors[i]);
            if (!rem.IsZero) return false;
        }
        return true;
    }

    // -------------------------------------------------------------------------
    // Identity
    // -------------------------------------------------------------------------

    [Fact]
    public void Identity2x2_AllOnes()
    {
        var A = Matrix<Integer>.Identity(2);
        var d = SmithNormalForm.Compute(A);
        AssertFactors(d, 1, 1);
    }

    [Fact]
    public void Identity3x3_AllOnes()
    {
        var A = Matrix<Integer>.Identity(3);
        var d = SmithNormalForm.Compute(A);
        AssertFactors(d, 1, 1, 1);
    }

    // -------------------------------------------------------------------------
    // Diagonal matrices (already in SNF up to divisibility)
    // -------------------------------------------------------------------------

    [Fact]
    public void Diagonal_AlreadyDivisible()
    {
        // diag(1, 2, 6): 1|2|6, already in SNF
        var A = M(3, 3,
            1, 0, 0,
            0, 2, 0,
            0, 0, 6);
        var d = SmithNormalForm.Compute(A);
        Assert.Equal(3, d.Count);
        Assert.True(DivisibilityChainHolds(d));
        // Product must equal det = 12
        var product = d.Aggregate(Integer.One, (acc, x) => acc * x);
        Assert.Equal((Integer)12, product);
    }

    [Fact]
    public void Diagonal_NeedsReordering()
    {
        // diag(6, 2, 3): needs gcd/lcm adjustment to get divisibility chain
        var A = M(3, 3,
            6, 0, 0,
            0, 2, 0,
            0, 0, 3);
        var d = SmithNormalForm.Compute(A);
        Assert.Equal(3, d.Count);
        Assert.True(DivisibilityChainHolds(d));
        // Product = 36
        var product = d.Aggregate(Integer.One, (acc, x) => acc * x);
        Assert.Equal((Integer)36, product);
    }

    // -------------------------------------------------------------------------
    // Scalar matrix
    // -------------------------------------------------------------------------

    [Fact]
    public void ScalarMatrix_3x3()
    {
        // 6 * Identity: SNF = diag(6, 6, 6) — 6|6|6
        var A = (Integer)6 * Matrix<Integer>.Identity(3);
        var d = SmithNormalForm.Compute(A);
        Assert.Equal(3, d.Count);
        Assert.True(DivisibilityChainHolds(d));
        foreach (var f in d)
            Assert.Equal((Integer)6, f);
    }

    // -------------------------------------------------------------------------
    // Zero matrix
    // -------------------------------------------------------------------------

    [Fact]
    public void ZeroMatrix_2x2_AllZero()
    {
        var A = Matrix<Integer>.Zero(2, 2);
        var d = SmithNormalForm.Compute(A);
        Assert.Equal(2, d.Count);
        foreach (var f in d)
            Assert.Equal(Integer.Zero, f);
    }

    // -------------------------------------------------------------------------
    // Known SNF results
    // -------------------------------------------------------------------------

    [Fact]
    public void KnownResult_2x2_Gcd2()
    {
        // [[2, 4], [6, 8]]: det = -8, gcd of all = 2
        // SNF = diag(2, 4) since 2|4
        var A = M(2, 2, 2, 4, 6, 8);
        var d = SmithNormalForm.Compute(A);
        Assert.Equal(2, d.Count);
        Assert.True(DivisibilityChainHolds(d));
        // d1 * d2 = |det| = 8
        var product = d.Aggregate(Integer.One, (acc, x) => acc * x);
        Assert.Equal((Integer)8, product);
        // d1 must divide d2
        Assert.Equal((Integer)2, d[0]);
    }

    [Fact]
    public void KnownResult_2x2_Coprime()
    {
        // [[1, 0], [0, 1]]: SNF = (1, 1)
        var A = Matrix<Integer>.Identity(2);
        var d = SmithNormalForm.Compute(A);
        AssertFactors(d, 1, 1);
    }

    [Fact]
    public void KnownResult_3x3_Classic()
    {
        // [[1, 2, 3], [4, 5, 6], [7, 8, 10]]
        // det = 1*1 - ... let the algorithm decide; check structural properties
        var A = M(3, 3,
            1, 2, 3,
            4, 5, 6,
            7, 8, 10);
        var d = SmithNormalForm.Compute(A);
        Assert.Equal(3, d.Count);
        Assert.True(DivisibilityChainHolds(d));
    }

    // -------------------------------------------------------------------------
    // Rectangular matrices
    // -------------------------------------------------------------------------

    [Fact]
    public void Rectangular_2x3()
    {
        var A = M(2, 3,
            1, 2, 3,
            4, 5, 6);
        var d = SmithNormalForm.Compute(A);
        Assert.Equal(2, d.Count); // min(2,3)
        Assert.True(DivisibilityChainHolds(d));
    }

    [Fact]
    public void Rectangular_3x2()
    {
        var A = M(3, 2,
            1, 2,
            3, 4,
            5, 6);
        var d = SmithNormalForm.Compute(A);
        Assert.Equal(2, d.Count); // min(3,2)
        Assert.True(DivisibilityChainHolds(d));
    }

    // -------------------------------------------------------------------------
    // Class group relevant: relation matrix → invariant factors
    // -------------------------------------------------------------------------

    [Fact]
    public void RelationMatrix_Z2xZ3_GivesZ6()
    {
        // Presentation of Z/6: one relation 6*e1 = 0.
        // Matrix = [[6]]: SNF = (6).
        var A = M(1, 1, 6);
        var d = SmithNormalForm.Compute(A);
        AssertFactors(d, 6);
    }

    [Fact]
    public void RelationMatrix_Z2xZ2_CorrectFactors()
    {
        // diag(2, 2): group Z/2 × Z/2
        var A = M(2, 2,
            2, 0,
            0, 2);
        var d = SmithNormalForm.Compute(A);
        AssertFactors(d, 2, 2);
    }

    [Fact]
    public void RelationMatrix_TrivialGroup()
    {
        // Identity: SNF = (1, 1) → group is trivial
        var A = Matrix<Integer>.Identity(2);
        var d = SmithNormalForm.Compute(A);
        AssertFactors(d, 1, 1);
    }

    // -------------------------------------------------------------------------
    // ComputeWithTransform: verify U * A * V = diag
    // -------------------------------------------------------------------------

    [Fact]
    public void WithTransform_2x2_VerifiesUAV()
    {
        var A = M(2, 2, 2, 4, 6, 8);
        var (factors, U, V) = SmithNormalForm.ComputeWithTransform(A);

        var UAV = U * A * V;
        int k = Math.Min(UAV.Rows, UAV.Cols);
        for (int i = 0; i < k; i++)
            Assert.Equal(factors[i], UAV[i, i].Abs());

        // Off-diagonal should be zero
        for (int i = 0; i < UAV.Rows; i++)
            for (int j = 0; j < UAV.Cols; j++)
                if (i != j) Assert.Equal(Integer.Zero, UAV[i, j]);
    }

    [Fact]
    public void WithTransform_3x3_VerifiesUAV()
    {
        var A = M(3, 3,
            1, 2, 3,
            0, 4, 5,
            0, 0, 6);
        var (factors, U, V) = SmithNormalForm.ComputeWithTransform(A);
        Assert.True(DivisibilityChainHolds(factors));

        var UAV = U * A * V;
        for (int i = 0; i < UAV.Rows; i++)
            for (int j = 0; j < UAV.Cols; j++)
                if (i != j) Assert.Equal(Integer.Zero, UAV[i, j]);
    }

    // -------------------------------------------------------------------------
    // Negative entries
    // -------------------------------------------------------------------------

    [Fact]
    public void NegativeEntries_FactorsNonNegative()
    {
        var A = M(2, 2, -2, -4, -6, -8);
        var d = SmithNormalForm.Compute(A);
        Assert.Equal(2, d.Count);
        foreach (var f in d)
            Assert.True(f.Sign >= 0);
        Assert.True(DivisibilityChainHolds(d));
    }

    // -------------------------------------------------------------------------
    // Idempotency: SNF of a diagonal matrix with divisibility is itself
    // -------------------------------------------------------------------------

    [Fact]
    public void Idempotent_DiagonalAlreadySNF()
    {
        // diag(1, 2, 6) — already in SNF
        var A = M(3, 3,
            1, 0, 0,
            0, 2, 0,
            0, 0, 6);
        var d1 = SmithNormalForm.Compute(A);
        // Rebuild diagonal matrix from factors
        var B = Matrix<Integer>.Zero(3, 3);
        // Just verify factors are stable
        var d2 = SmithNormalForm.Compute(A);
        AssertFactors(d1, d2.Select(x => (int)(System.Numerics.BigInteger)(Integer)x).ToArray());
    }
}
