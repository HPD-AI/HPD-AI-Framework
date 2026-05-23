using Helium.Algebra;
using Helium.Primitives;

namespace Helium.Algorithms;

/// <summary>
/// Exact fraction-free linear solving.
/// The generic APIs return numerator data plus the determinant denominator,
/// avoiding any implicit fraction-field construction.
/// </summary>
public static class FractionFreeLinearSolve
{
    public static (Vector<R> Numerators, R Denominator)? SolveCramer<R>(Matrix<R> a, Vector<R> b)
        where R : ICommRing<R>, IEuclideanDomain<R>
    {
        ValidateSquareSystem(a, b);

        var det = Determinant.Compute(a);
        if (det.Equals(R.AdditiveIdentity))
            return null;

        var n = a.Rows;
        var result = new R[n];
        for (int col = 0; col < n; col++)
        {
            var replaced = ReplaceColumn(a, b, col);
            result[col] = Determinant.Compute(replaced);
        }

        return (Vector<R>.FromArray(result), det);
    }

    public static (Matrix<R> Adjugate, R Determinant)? InverseAdjugate<R>(Matrix<R> a)
        where R : ICommRing<R>, IEuclideanDomain<R>
    {
        if (a.Rows != a.Cols)
            throw new ArgumentException("Matrix must be square.", nameof(a));

        var det = Determinant.Compute(a);
        if (det.Equals(R.AdditiveIdentity))
            return null;

        var n = a.Rows;
        var result = new R[n * n];
        for (int row = 0; row < n; row++)
        for (int col = 0; col < n; col++)
        {
            var cofactor = MinorDeterminant(a, row, col);
            if (((row + col) & 1) != 0)
                cofactor = -cofactor;

            result[col * n + row] = cofactor;
        }

        return (Matrix<R>.FromArray(n, n, result), det);
    }

    public static Vector<Rational>? Solve(Matrix<Integer> a, Vector<Integer> b)
    {
        var solved = SolveCramer(a, b);
        if (solved is null)
            return null;

        var (numerators, det) = solved.Value;
        var n = numerators.Length;
        var result = new Rational[n];
        for (int i = 0; i < n; i++)
            result[i] = Rational.Create(numerators[i], det);

        return Vector<Rational>.FromArray(result);
    }

    public static Matrix<Rational>? Inverse(Matrix<Integer> a)
    {
        var inverse = InverseAdjugate(a);
        if (inverse is null)
            return null;

        var (adjugate, det) = inverse.Value;
        var n = a.Rows;
        var result = new Rational[n * n];
        for (int row = 0; row < n; row++)
        for (int col = 0; col < n; col++)
            result[row * n + col] = Rational.Create(adjugate[row, col], det);

        return Matrix<Rational>.FromArray(n, n, result);
    }

    private static void ValidateSquareSystem<R>(Matrix<R> a, Vector<R> b)
        where R : ICommRing<R>, IEuclideanDomain<R>
    {
        if (a.Rows != a.Cols)
            throw new ArgumentException("Matrix must be square.", nameof(a));
        if (b.Length != a.Rows)
            throw new ArgumentException("Vector dimension must match matrix rows.", nameof(b));
    }

    private static Matrix<R> ReplaceColumn<R>(Matrix<R> a, Vector<R> b, int column)
        where R : ICommRing<R>, IEuclideanDomain<R>
    {
        var n = a.Rows;
        var data = new R[n * n];
        for (int row = 0; row < n; row++)
        for (int col = 0; col < n; col++)
            data[row * n + col] = col == column ? b[row] : a[row, col];
        return Matrix<R>.FromArray(n, n, data);
    }

    private static R MinorDeterminant<R>(Matrix<R> a, int skipRow, int skipCol)
        where R : ICommRing<R>, IEuclideanDomain<R>
    {
        var n = a.Rows;
        if (n == 1)
            return R.MultiplicativeIdentity;

        var data = new R[(n - 1) * (n - 1)];
        var index = 0;
        for (int row = 0; row < n; row++)
        {
            if (row == skipRow)
                continue;

            for (int col = 0; col < n; col++)
            {
                if (col == skipCol)
                    continue;

                data[index++] = a[row, col];
            }
        }

        return Determinant.Compute(Matrix<R>.FromArray(n - 1, n - 1, data));
    }
}
