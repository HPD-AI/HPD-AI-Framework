namespace HPD.ML.Backends.Pjrt;

internal static class PjrtBackendCore
{
    public const int MaxGeneratedSolveSize = 8;

    public static void ValidateMatrixShape(int rows, int cols)
    {
        if (rows <= 0)
            throw new ArgumentOutOfRangeException(nameof(rows), "Rows must be positive.");
        if (cols <= 0)
            throw new ArgumentOutOfRangeException(nameof(cols), "Columns must be positive.");
    }

    public static void ValidateSlice(int rows, int cols, int startRow, int startCol, int rowCount, int colCount)
    {
        if (startRow < 0)
            throw new ArgumentOutOfRangeException(nameof(startRow));
        if (startCol < 0)
            throw new ArgumentOutOfRangeException(nameof(startCol));
        if (rowCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(rowCount));
        if (colCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(colCount));
        if (startRow + rowCount > rows || startCol + colCount > cols)
            throw new ArgumentException("Slice range must be inside tensor bounds.");
    }

    public static void ValidateGeneratedSolveSize(int n, string operationName)
    {
        if (n is < 1 or > MaxGeneratedSolveSize)
            throw new NotSupportedException($"XLA {operationName} currently supports matrix sizes 1 through {MaxGeneratedSolveSize}.");
    }

    public static PivotPlan PlanPivoting(ReadOnlySpan<float> matrix, int n, float tolerance)
    {
        var work = matrix.ToArray();
        var permutation = Enumerable.Range(0, n).ToArray();
        var swaps = new List<(int A, int B)>();

        for (var pivotIndex = 0; pivotIndex < n; pivotIndex++)
        {
            var bestRow = pivotIndex;
            var bestMagnitude = Math.Abs(work[pivotIndex * n + pivotIndex]);
            for (var row = pivotIndex + 1; row < n; row++)
            {
                var magnitude = Math.Abs(work[row * n + pivotIndex]);
                if (magnitude > bestMagnitude)
                {
                    bestMagnitude = magnitude;
                    bestRow = row;
                }
            }

            if (bestMagnitude <= tolerance)
                throw new ArithmeticException("XLA generated solve encountered a singular or near-singular pivot column.");

            if (bestRow != pivotIndex)
            {
                SwapRows(work, n, pivotIndex, bestRow);
                (permutation[pivotIndex], permutation[bestRow]) = (permutation[bestRow], permutation[pivotIndex]);
                swaps.Add((pivotIndex, bestRow));
            }

            var pivot = work[pivotIndex * n + pivotIndex];
            for (var col = pivotIndex; col < n; col++)
                work[pivotIndex * n + col] /= pivot;

            for (var row = 0; row < n; row++)
            {
                if (row == pivotIndex)
                    continue;

                var factor = work[row * n + pivotIndex];
                for (var col = pivotIndex; col < n; col++)
                    work[row * n + col] -= factor * work[pivotIndex * n + col];
            }
        }

        return new PivotPlan(permutation, swaps.Count != 0);
    }

    public static PivotPlan PlanPivoting(ReadOnlySpan<double> matrix, int n, double tolerance)
    {
        var work = matrix.ToArray();
        var permutation = Enumerable.Range(0, n).ToArray();
        var swaps = new List<(int A, int B)>();

        for (var pivotIndex = 0; pivotIndex < n; pivotIndex++)
        {
            var bestRow = pivotIndex;
            var bestMagnitude = Math.Abs(work[pivotIndex * n + pivotIndex]);
            for (var row = pivotIndex + 1; row < n; row++)
            {
                var magnitude = Math.Abs(work[row * n + pivotIndex]);
                if (magnitude > bestMagnitude)
                {
                    bestMagnitude = magnitude;
                    bestRow = row;
                }
            }

            if (bestMagnitude <= tolerance)
                throw new ArithmeticException("XLA generated solve encountered a singular or near-singular pivot column.");

            if (bestRow != pivotIndex)
            {
                SwapRows(work, n, pivotIndex, bestRow);
                (permutation[pivotIndex], permutation[bestRow]) = (permutation[bestRow], permutation[pivotIndex]);
                swaps.Add((pivotIndex, bestRow));
            }

            var pivot = work[pivotIndex * n + pivotIndex];
            for (var col = pivotIndex; col < n; col++)
                work[pivotIndex * n + col] /= pivot;

            for (var row = 0; row < n; row++)
            {
                if (row == pivotIndex)
                    continue;

                var factor = work[row * n + pivotIndex];
                for (var col = pivotIndex; col < n; col++)
                    work[row * n + col] -= factor * work[pivotIndex * n + col];
            }
        }

        return new PivotPlan(permutation, swaps.Count != 0);
    }

    public static bool TryDetectTriangular(ReadOnlySpan<float> matrix, int n, float tolerance, out bool lower)
    {
        var hasUpperEntries = false;
        var hasLowerEntries = false;

        for (var row = 0; row < n; row++)
        {
            if (Math.Abs(matrix[row * n + row]) <= tolerance)
            {
                lower = false;
                return false;
            }

            for (var col = 0; col < n; col++)
            {
                if (row == col || Math.Abs(matrix[row * n + col]) <= tolerance)
                    continue;
                if (col > row)
                    hasUpperEntries = true;
                else
                    hasLowerEntries = true;
            }
        }

        if (hasUpperEntries && hasLowerEntries)
        {
            lower = false;
            return false;
        }

        lower = hasLowerEntries || !hasUpperEntries;
        return true;
    }

    public static bool TryDetectTriangular(ReadOnlySpan<double> matrix, int n, double tolerance, out bool lower)
    {
        var hasUpperEntries = false;
        var hasLowerEntries = false;

        for (var row = 0; row < n; row++)
        {
            if (Math.Abs(matrix[row * n + row]) <= tolerance)
            {
                lower = false;
                return false;
            }

            for (var col = 0; col < n; col++)
            {
                if (row == col || Math.Abs(matrix[row * n + col]) <= tolerance)
                    continue;
                if (col > row)
                    hasUpperEntries = true;
                else
                    hasLowerEntries = true;
            }
        }

        if (hasUpperEntries && hasLowerEntries)
        {
            lower = false;
            return false;
        }

        lower = hasLowerEntries || !hasUpperEntries;
        return true;
    }

    public static bool IsSymmetricPositiveDefinite(ReadOnlySpan<float> matrix, int n, float tolerance)
    {
        for (var row = 0; row < n; row++)
            for (var col = row + 1; col < n; col++)
                if (Math.Abs(matrix[row * n + col] - matrix[col * n + row]) > tolerance)
                    return false;

        var lower = new float[checked(n * n)];
        for (var row = 0; row < n; row++)
        {
            for (var col = 0; col <= row; col++)
            {
                var sum = matrix[row * n + col];
                for (var k = 0; k < col; k++)
                    sum -= lower[row * n + k] * lower[col * n + k];

                if (row == col)
                {
                    if (sum <= tolerance)
                        return false;
                    lower[row * n + col] = MathF.Sqrt(sum);
                }
                else
                {
                    lower[row * n + col] = sum / lower[col * n + col];
                }
            }
        }

        return true;
    }

    public static bool IsSymmetricPositiveDefinite(ReadOnlySpan<double> matrix, int n, double tolerance)
    {
        for (var row = 0; row < n; row++)
            for (var col = row + 1; col < n; col++)
                if (Math.Abs(matrix[row * n + col] - matrix[col * n + row]) > tolerance)
                    return false;

        var lower = new double[checked(n * n)];
        for (var row = 0; row < n; row++)
        {
            for (var col = 0; col <= row; col++)
            {
                var sum = matrix[row * n + col];
                for (var k = 0; k < col; k++)
                    sum -= lower[row * n + k] * lower[col * n + k];

                if (row == col)
                {
                    if (sum <= tolerance)
                        return false;
                    lower[row * n + col] = Math.Sqrt(sum);
                }
                else
                {
                    lower[row * n + col] = sum / lower[col * n + col];
                }
            }
        }

        return true;
    }

    public static float[] ApplyRowPermutation(ReadOnlySpan<float> data, int rows, int cols, int[] permutation)
    {
        var result = new float[data.Length];
        for (var row = 0; row < rows; row++)
            data.Slice(permutation[row] * cols, cols).CopyTo(result.AsSpan(row * cols, cols));
        return result;
    }

    public static double[] ApplyRowPermutation(ReadOnlySpan<double> data, int rows, int cols, int[] permutation)
    {
        var result = new double[data.Length];
        for (var row = 0; row < rows; row++)
            data.Slice(permutation[row] * cols, cols).CopyTo(result.AsSpan(row * cols, cols));
        return result;
    }

    public static int[] ApplyRowPermutation(ReadOnlySpan<int> data, int rows, int cols, int[] permutation)
    {
        var result = new int[data.Length];
        for (var row = 0; row < rows; row++)
            data.Slice(permutation[row] * cols, cols).CopyTo(result.AsSpan(row * cols, cols));
        return result;
    }

    public static long[] ApplyRowPermutation(ReadOnlySpan<long> data, int rows, int cols, int[] permutation)
    {
        var result = new long[data.Length];
        for (var row = 0; row < rows; row++)
            data.Slice(permutation[row] * cols, cols).CopyTo(result.AsSpan(row * cols, cols));
        return result;
    }

    public static float[] IdentityFloat(int n)
    {
        var data = new float[checked(n * n)];
        for (var i = 0; i < n; i++)
            data[i * n + i] = 1.0f;
        return data;
    }

    public static double[] IdentityDouble(int n)
    {
        var data = new double[checked(n * n)];
        for (var i = 0; i < n; i++)
            data[i * n + i] = 1.0;
        return data;
    }

    private static void SwapRows(float[] matrix, int n, int a, int b)
    {
        for (var col = 0; col < n; col++)
            (matrix[a * n + col], matrix[b * n + col]) = (matrix[b * n + col], matrix[a * n + col]);
    }

    private static void SwapRows(double[] matrix, int n, int a, int b)
    {
        for (var col = 0; col < n; col++)
            (matrix[a * n + col], matrix[b * n + col]) = (matrix[b * n + col], matrix[a * n + col]);
    }
}

internal sealed record PivotPlan(int[] RowPermutation, bool RequiresPermutation);
