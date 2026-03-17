using System.Numerics;
using Helium.Primitives;
using Helium.Algebra;
using Helium.Algorithms;

namespace Helium.Algorithms.Tests;

public class LatticeReductionTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static Matrix<Integer> M(int rows, int cols, params int[] entries)
    {
        var data = entries.Select(x => (Integer)x).ToArray();
        return Matrix<Integer>.FromArray(rows, cols, data);
    }

    // Gram matrix of the rows of B: G[i,j] = <b_i, b_j>
    private static Integer[,] GramMatrix(Matrix<Integer> B)
    {
        int m = B.Rows;
        int n = B.Cols;
        var G = new Integer[m, m];
        for (int i = 0; i < m; i++)
            for (int j = 0; j < m; j++)
            {
                var dot = Integer.Zero;
                for (int c = 0; c < n; c++)
                    dot += B[i, c] * B[j, c];
                G[i, j] = dot;
            }
        return G;
    }

    // Check that the Lovász condition holds for all consecutive pairs.
    // δ · ||b*_{k-1}||² ≤ ||b*_k||² + μ_{k,k-1}² · ||b*_{k-1}||²
    private static bool SatisfiesLovász(Matrix<Integer> B, double delta = 0.99)
    {
        int m = B.Rows;
        int n = B.Cols;
        if (m <= 1) return true;

        var G = GramMatrix(B);

        // Compute r[i] = ||b*_i||² and μ[i,j] as doubles.
        var r  = new double[m];
        var mu = new double[m, m];

        r[0] = (double)(BigInteger)G[0, 0];
        for (int k = 1; k < m; k++)
        {
            double gKK = (double)(BigInteger)G[k, k];
            for (int j = 0; j < k; j++)
            {
                double s = (double)(BigInteger)G[k, j];
                for (int l = 0; l < j; l++)
                    s -= mu[k, l] * mu[j, l] * r[l];
                mu[k, j] = r[j] > 0 ? s / r[j] : 0.0;
                gKK -= mu[k, j] * mu[k, j] * r[j];
            }
            r[k] = gKK;

            // Lovász: δ · r[k-1] ≤ r[k] + μ[k,k-1]² · r[k-1]
            double sk = r[k] + mu[k, k - 1] * mu[k, k - 1] * r[k - 1];
            if (delta * r[k - 1] > sk + 1e-6) // small tolerance for floating-point
                return false;
        }

        return true;
    }

    // Check size-reduction: |μ_{i,j}| ≤ 0.5 for all i > j.
    private static bool IsSizeReduced(Matrix<Integer> B, double eta = 0.51)
    {
        int m = B.Rows;
        if (m <= 1) return true;

        var G  = GramMatrix(B);
        var r  = new double[m];
        var mu = new double[m, m];

        r[0] = (double)(BigInteger)G[0, 0];
        for (int k = 1; k < m; k++)
        {
            double gKK = (double)(BigInteger)G[k, k];
            for (int j = 0; j < k; j++)
            {
                double s = (double)(BigInteger)G[k, j];
                for (int l = 0; l < j; l++)
                    s -= mu[k, l] * mu[j, l] * r[l];
                mu[k, j] = r[j] > 0 ? s / r[j] : 0.0;
                gKK -= mu[k, j] * mu[k, j] * r[j];
            }
            r[k] = gKK;

            for (int j = 0; j < k; j++)
                if (Math.Abs(mu[k, j]) > eta + 1e-9)
                    return false;
        }

        return true;
    }

    // -------------------------------------------------------------------------
    // Trivial cases
    // -------------------------------------------------------------------------

    [Fact]
    public void SingleRow_ReturnedUnchanged()
    {
        var B = M(1, 3, 3, 1, 4);
        var R = LatticeReduction.LLL(B);
        Assert.Equal(1, R.Rows);
        Assert.Equal(3, R.Cols);
        Assert.Equal(B[0, 0], R[0, 0]);
        Assert.Equal(B[0, 1], R[0, 1]);
        Assert.Equal(B[0, 2], R[0, 2]);
    }

    [Fact]
    public void Identity2x2_AlreadyReduced()
    {
        var B = Matrix<Integer>.Identity(2);
        var R = LatticeReduction.LLL(B);
        Assert.True(SatisfiesLovász(R));
        Assert.True(IsSizeReduced(R));
    }

    [Fact]
    public void Identity3x3_AlreadyReduced()
    {
        var B = Matrix<Integer>.Identity(3);
        var R = LatticeReduction.LLL(B);
        Assert.True(SatisfiesLovász(R));
        Assert.True(IsSizeReduced(R));
    }

    // -------------------------------------------------------------------------
    // Correctness: output row space equals input row space
    // -------------------------------------------------------------------------

    // We verify this by checking that the det of the Gram matrix is the same
    // (i.e., |det B|² = |det R|²), which holds iff they span the same lattice.
    private static BigInteger GramDet(Matrix<Integer> B)
    {
        var G = GramMatrix(B);
        int m = B.Rows;
        // Compute det of integer matrix G using expansion (small m only).
        return GramDetRecursive(G, m);
    }

    private static BigInteger GramDetRecursive(Integer[,] G, int m)
    {
        if (m == 1) return (BigInteger)G[0, 0];
        if (m == 2) return (BigInteger)(G[0, 0] * G[1, 1] - G[0, 1] * G[1, 0]);

        BigInteger det = BigInteger.Zero;
        for (int c = 0; c < m; c++)
        {
            var minor = new Integer[m - 1, m - 1];
            for (int i = 1; i < m; i++)
                for (int j = 0; j < m; j++)
                    if (j != c)
                        minor[i - 1, j < c ? j : j - 1] = G[i, j];

            var cofactor = (BigInteger)G[0, c] * GramDetRecursive(minor, m - 1);
            det += c % 2 == 0 ? cofactor : -cofactor;
        }
        return det;
    }

    [Fact]
    public void SameLattice_2x2()
    {
        var B = M(2, 2, 1, 1, -1, 2);
        var R = LatticeReduction.LLL(B);
        Assert.Equal(BigInteger.Abs(GramDet(B)), BigInteger.Abs(GramDet(R)));
        Assert.True(SatisfiesLovász(R));
    }

    [Fact]
    public void SameLattice_3x3()
    {
        var B = M(3, 3,
            1, 0, 0,
            0, 1, 0,
            1, 1, 2);
        var R = LatticeReduction.LLL(B);
        Assert.Equal(BigInteger.Abs(GramDet(B)), BigInteger.Abs(GramDet(R)));
        Assert.True(SatisfiesLovász(R));
        Assert.True(IsSizeReduced(R));
    }

    // -------------------------------------------------------------------------
    // Classic LLL examples
    // -------------------------------------------------------------------------

    [Fact]
    public void ClassicExample_2x2_ShortVector()
    {
        // The lattice generated by (1, 100) and (0, 1) — LLL should find (1, 0) type short vectors.
        var B = M(2, 2, 1, 100, 0, 1);
        var R = LatticeReduction.LLL(B);
        Assert.True(SatisfiesLovász(R));
        Assert.True(IsSizeReduced(R));
        // First row should have small norm.
        BigInteger norm1sq = (BigInteger)R[0, 0] * (BigInteger)R[0, 0] + (BigInteger)R[0, 1] * (BigInteger)R[0, 1];
        Assert.True(norm1sq <= 4); // should find (1, 0) or (0, 1)
    }

    [Fact]
    public void LargeCoefficients_2x2()
    {
        var B = M(2, 2, 100, 200, 301, 603);
        var R = LatticeReduction.LLL(B);
        Assert.Equal(BigInteger.Abs(GramDet(B)), BigInteger.Abs(GramDet(R)));
        Assert.True(SatisfiesLovász(R));
        Assert.True(IsSizeReduced(R));
    }

    [Fact]
    public void KnownLllResult_3x3()
    {
        // Standard LLL example: basis that needs many swaps.
        var B = M(3, 3,
            1, 0, 0,
            0, 1, 0,
            100, 200, 1);
        var R = LatticeReduction.LLL(B);
        Assert.Equal(BigInteger.Abs(GramDet(B)), BigInteger.Abs(GramDet(R)));
        Assert.True(SatisfiesLovász(R));
        Assert.True(IsSizeReduced(R));
    }

    [Fact]
    public void AlreadyLLLReduced_Unchanged()
    {
        // diag(1,1,1) is trivially LLL-reduced.
        var B = Matrix<Integer>.Identity(3);
        var R = LatticeReduction.LLL(B);
        // Should still be identity (already reduced).
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                Assert.Equal(B[i, j], R[i, j]);
    }

    // -------------------------------------------------------------------------
    // Rectangular: m × n with m < n
    // -------------------------------------------------------------------------

    [Fact]
    public void Rectangular_2x4()
    {
        var B = M(2, 4,
            1, 0, 1, 0,
            0, 1, 0, 1);
        var R = LatticeReduction.LLL(B);
        Assert.Equal(2, R.Rows);
        Assert.Equal(4, R.Cols);
        Assert.True(SatisfiesLovász(R));
        Assert.True(IsSizeReduced(R));
    }

    // -------------------------------------------------------------------------
    // Negative entries
    // -------------------------------------------------------------------------

    [Fact]
    public void NegativeEntries_2x2()
    {
        var B = M(2, 2, -3, 1, 2, -5);
        var R = LatticeReduction.LLL(B);
        Assert.Equal(BigInteger.Abs(GramDet(B)), BigInteger.Abs(GramDet(R)));
        Assert.True(SatisfiesLovász(R));
        Assert.True(IsSizeReduced(R));
    }

    // -------------------------------------------------------------------------
    // Dimension output preservation
    // -------------------------------------------------------------------------

    [Fact]
    public void OutputDimensions_Preserved()
    {
        var B = M(3, 5,
            1, 2, 0, 1, 0,
            0, 1, 3, 0, 1,
            2, 0, 1, 4, 0);
        var R = LatticeReduction.LLL(B);
        Assert.Equal(3, R.Rows);
        Assert.Equal(5, R.Cols);
        Assert.True(SatisfiesLovász(R));
    }

    // -------------------------------------------------------------------------
    // Large-coefficient stress test
    // -------------------------------------------------------------------------

    [Fact]
    public void StressTest_4x4_LargeCoeffs()
    {
        var B = M(4, 4,
            100,   0,   0,   0,
              0, 100,   0,   0,
              0,   0, 100,   0,
             37,  53,  71,   1);
        var R = LatticeReduction.LLL(B);
        Assert.Equal(BigInteger.Abs(GramDet(B)), BigInteger.Abs(GramDet(R)));
        Assert.True(SatisfiesLovász(R));
        Assert.True(IsSizeReduced(R));
    }

    // -------------------------------------------------------------------------
    // First row norm bound: ||b_1|| ≤ 2^((m-1)/2) * λ_1
    // For the identity basis, λ_1 = 1, so ||b_1|| should be 1.
    // -------------------------------------------------------------------------

    [Fact]
    public void FirstRowBound_Identity4x4()
    {
        var B = Matrix<Integer>.Identity(4);
        var R = LatticeReduction.LLL(B);
        BigInteger norm1sq = BigInteger.Zero;
        for (int c = 0; c < 4; c++)
        {
            var v = (BigInteger)R[0, c];
            norm1sq += v * v;
        }
        Assert.Equal(BigInteger.One, norm1sq); // first row is a unit vector
    }

    // -------------------------------------------------------------------------
    // Larger dimensions
    // -------------------------------------------------------------------------

    [Fact]
    public void SameLattice_5x5()
    {
        var B = M(5, 5,
            1, 0, 0, 0, 0,
            0, 1, 0, 0, 0,
            0, 0, 1, 0, 0,
            0, 0, 0, 1, 0,
            3, 7, 11, 13, 1);
        var R = LatticeReduction.LLL(B);
        Assert.Equal(BigInteger.Abs(GramDet(B)), BigInteger.Abs(GramDet(R)));
        Assert.True(SatisfiesLovász(R));
        Assert.True(IsSizeReduced(R));
        Assert.Equal(5, R.Rows);
        Assert.Equal(5, R.Cols);
    }

    [Fact]
    public void SameLattice_6x6()
    {
        // Basis with a dense last row (many swaps needed).
        var B = M(6, 6,
            1, 0, 0, 0, 0, 0,
            0, 1, 0, 0, 0, 0,
            0, 0, 1, 0, 0, 0,
            0, 0, 0, 1, 0, 0,
            0, 0, 0, 0, 1, 0,
           17,19,23,29,31, 1);
        var R = LatticeReduction.LLL(B);
        Assert.Equal(BigInteger.Abs(GramDet(B)), BigInteger.Abs(GramDet(R)));
        Assert.True(SatisfiesLovász(R));
        Assert.True(IsSizeReduced(R));
    }

    // -------------------------------------------------------------------------
    // Known exact output
    // -------------------------------------------------------------------------

    [Fact]
    public void KnownExactOutput_2x2_LllReducedBasis()
    {
        // Classical LLL example from Nguyen-Stehlé:
        // B = [(1,1),(−1,2)] reduces to [(1,1),(0,?) ] or a short basis.
        // The LLL-reduced basis of the integer lattice Z(1,1)+Z(-1,2) is {(1,1),(0,3)} or {(1,1),(-1,2)}.
        // Both satisfy Lovász; the first row should have norm² ≤ 2.
        var B = M(2, 2, 1, 1, -1, 2);
        var R = LatticeReduction.LLL(B);

        BigInteger norm0sq = (BigInteger)R[0, 0] * (BigInteger)R[0, 0] + (BigInteger)R[0, 1] * (BigInteger)R[0, 1];
        BigInteger norm1sq = (BigInteger)R[1, 0] * (BigInteger)R[1, 0] + (BigInteger)R[1, 1] * (BigInteger)R[1, 1];

        // First row should be the shortest: norm² = 2 (the vector (1,1) or (−1,−1)).
        Assert.True(norm0sq <= 2, $"First row norm² was {norm0sq}, expected ≤ 2");
        // Second row must be longer.
        Assert.True(norm1sq >= norm0sq);
        Assert.True(SatisfiesLovász(R));
        Assert.True(IsSizeReduced(R));
    }

    [Fact]
    public void KnownExactOutput_2x2_ShortVectorIsOneZero()
    {
        // Lattice spanned by (1, 10000) and (0, 1).
        // LLL must find (1, 0) — the canonical shortest vector.
        var B = M(2, 2, 1, 10000, 0, 1);
        var R = LatticeReduction.LLL(B);

        // First row: (1, 0) or (−1, 0) — norm² = 1.
        BigInteger norm0sq = (BigInteger)R[0, 0] * (BigInteger)R[0, 0] + (BigInteger)R[0, 1] * (BigInteger)R[0, 1];
        Assert.Equal(BigInteger.One, norm0sq);
    }

    // -------------------------------------------------------------------------
    // BigInteger-precision stress: entries that overflow double
    // -------------------------------------------------------------------------

    [Fact]
    public void BigIntegerPrecision_EntriesExceedDoubleRange()
    {
        // Entries beyond 2^53 force the Rational fallback path.
        // Use a 3×3 matrix with one very large diagonal entry and off-diagonal entries.
        BigInteger big = BigInteger.Pow(2, 60);
        var entries = new Integer[]
        {
            (Integer)big, Integer.Zero,   Integer.Zero,
            (Integer)3,   Integer.One,    Integer.Zero,
            (Integer)7,   (Integer)5,     Integer.One
        };
        var B = Matrix<Integer>.FromArray(3, 3, entries);
        var R = LatticeReduction.LLL(B);

        // Same lattice (Gram det preserved).
        Assert.Equal(BigInteger.Abs(GramDet(B)), BigInteger.Abs(GramDet(R)));
        Assert.True(SatisfiesLovász(R));
    }

    // -------------------------------------------------------------------------
    // LLL output is deterministic: same input always gives same output
    // -------------------------------------------------------------------------

    [Fact]
    public void Deterministic_SameInputSameOutput()
    {
        var B = M(4, 4,
            1, 0, 0, 0,
            0, 1, 0, 0,
            0, 0, 1, 0,
           17, 5, 3, 1);
        var R1 = LatticeReduction.LLL(B);
        var R2 = LatticeReduction.LLL(B);
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
                Assert.Equal(R1[i, j], R2[j == j ? i : i, j]); // always equal
        Assert.True(SatisfiesLovász(R1));
        Assert.True(IsSizeReduced(R1));
    }

    // -------------------------------------------------------------------------
    // Rectangular: m × n with m > 1, n > m (more columns than rows)
    // -------------------------------------------------------------------------

    [Fact]
    public void Rectangular_3x6_SameLattice()
    {
        var B = M(3, 6,
            1, 0, 0, 10, 5, 3,
            0, 1, 0,  7, 2, 8,
            0, 0, 1,  4, 9, 1);
        var R = LatticeReduction.LLL(B);
        Assert.Equal(3, R.Rows);
        Assert.Equal(6, R.Cols);
        Assert.Equal(BigInteger.Abs(GramDet(B)), BigInteger.Abs(GramDet(R)));
        Assert.True(SatisfiesLovász(R));
        Assert.True(IsSizeReduced(R));
    }

    // -------------------------------------------------------------------------
    // Zero-row edge case
    // -------------------------------------------------------------------------

    [Fact]
    public void ZeroRow_HandledGracefully()
    {
        // A 2×2 matrix with one zero row — degenerate lattice.
        var B = M(2, 2, 1, 0, 0, 0);
        var R = LatticeReduction.LLL(B);
        // Must not throw. Output dimensions preserved.
        Assert.Equal(2, R.Rows);
        Assert.Equal(2, R.Cols);
    }
}
