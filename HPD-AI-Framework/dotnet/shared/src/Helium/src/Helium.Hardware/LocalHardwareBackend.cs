using System.Numerics.Tensors;

namespace Helium.Hardware;

/// <summary>
/// Local CPU backend for hardware tensors. Operations are immutable: inputs are not mutated.
/// Currently supports double and float element types.
/// </summary>
public sealed class LocalHardwareBackend<T> : IExecutionBackend<T> where T : unmanaged
{
    public IHardwareTensor<T> CreateMatrix(int rows, int cols, ReadOnlySpan<T> initialData = default) =>
        new HardwareTensor<T>(rows, cols, initialData);

    public IHardwareTensor<T> MatMul(IHardwareTensor<T> left, IHardwareTensor<T> right)
    {
        var a = RequireLocal(left);
        var b = RequireLocal(right);
        if (a.Cols != b.Rows)
            throw new ArgumentException("Matrix dimensions are incompatible for multiplication.");

        if (typeof(T) == typeof(double))
            return CastResult(MatMulDouble(CastSpan<double>(a.Data), CastSpan<double>(b.Data), a.Rows, a.Cols, b.Cols));
        if (typeof(T) == typeof(float))
            return CastResult(MatMulFloat(CastSpan<float>(a.Data), CastSpan<float>(b.Data), a.Rows, a.Cols, b.Cols));

        throw UnsupportedType();
    }

    public IHardwareTensor<T> MatrixInverse(IHardwareTensor<T> value)
    {
        var tensor = RequireLocal(value);
        if (tensor.Rows != tensor.Cols)
            throw new ArgumentException("Only square matrices can be inverted.", nameof(value));

        if (typeof(T) == typeof(double))
            return CastResult(SolveDouble(
                CastSpan<double>(tensor.Data),
                IdentityDouble(tensor.Rows),
                tensor.Rows,
                tensor.Rows));
        if (typeof(T) == typeof(float))
            return CastResult(SolveFloat(
                CastSpan<float>(tensor.Data),
                IdentityFloat(tensor.Rows),
                tensor.Rows,
                tensor.Rows));

        throw UnsupportedType();
    }

    public IHardwareTensor<T> LinearSolve(IHardwareTensor<T> matrix, IHardwareTensor<T> rightHandSide)
    {
        var a = RequireLocal(matrix);
        var b = RequireLocal(rightHandSide);
        if (a.Rows != a.Cols)
            throw new ArgumentException("Coefficient matrix must be square.", nameof(matrix));
        if (b.Rows != a.Rows)
            throw new ArgumentException("Right-hand side row count must match matrix dimension.", nameof(rightHandSide));

        if (typeof(T) == typeof(double))
            return CastResult(SolveDouble(CastSpan<double>(a.Data), CastSpan<double>(b.Data), a.Rows, b.Cols));
        if (typeof(T) == typeof(float))
            return CastResult(SolveFloat(CastSpan<float>(a.Data), CastSpan<float>(b.Data), a.Rows, b.Cols));

        throw UnsupportedType();
    }

    public IHardwareTensor<T> Transpose(IHardwareTensor<T> value)
    {
        var tensor = RequireLocal(value);

        if (typeof(T) == typeof(double))
        {
            var data = CastSpan<double>(tensor.Data);
            var result = new double[data.Length];
            for (var r = 0; r < tensor.Rows; r++)
                for (var c = 0; c < tensor.Cols; c++)
                    result[c * tensor.Rows + r] = data[r * tensor.Cols + c];
            return CastResult(new HardwareTensor<double>(tensor.Cols, tensor.Rows, result));
        }

        if (typeof(T) == typeof(float))
        {
            var data = CastSpan<float>(tensor.Data);
            var result = new float[data.Length];
            for (var r = 0; r < tensor.Rows; r++)
                for (var c = 0; c < tensor.Cols; c++)
                    result[c * tensor.Rows + r] = data[r * tensor.Cols + c];
            return CastResult(new HardwareTensor<float>(tensor.Cols, tensor.Rows, result));
        }

        throw UnsupportedType();
    }

    public IHardwareTensor<T> Add(IHardwareTensor<T> left, IHardwareTensor<T> right) =>
        Elementwise(left, right, TensorElementwiseOp.Add);

    public IHardwareTensor<T> Subtract(IHardwareTensor<T> left, IHardwareTensor<T> right) =>
        Elementwise(left, right, TensorElementwiseOp.Subtract);

    public IHardwareTensor<T> Multiply(IHardwareTensor<T> left, IHardwareTensor<T> right) =>
        Elementwise(left, right, TensorElementwiseOp.Multiply);

    public IHardwareTensor<T> Negate(IHardwareTensor<T> value)
    {
        var tensor = RequireLocal(value);
        if (typeof(T) == typeof(double))
        {
            var data = CastSpan<double>(tensor.Data);
            var result = new HardwareTensor<double>(tensor.Rows, tensor.Cols);
            TensorPrimitives.Negate(data, result.MutableData.Raw);
            return CastResult(result);
        }
        if (typeof(T) == typeof(float))
        {
            var data = CastSpan<float>(tensor.Data);
            var result = new HardwareTensor<float>(tensor.Rows, tensor.Cols);
            TensorPrimitives.Negate(data, result.MutableData.Raw);
            return CastResult(result);
        }

        throw UnsupportedType();
    }

    public IHardwareTensor<T> Scale(IHardwareTensor<T> value, T scalar)
    {
        var tensor = RequireLocal(value);
        if (typeof(T) == typeof(double))
        {
            var data = CastSpan<double>(tensor.Data);
            var result = new HardwareTensor<double>(tensor.Rows, tensor.Cols);
            TensorPrimitives.Multiply(data, (double)(object)scalar, result.MutableData.Raw);
            return CastResult(result);
        }
        if (typeof(T) == typeof(float))
        {
            var data = CastSpan<float>(tensor.Data);
            var result = new HardwareTensor<float>(tensor.Rows, tensor.Cols);
            TensorPrimitives.Multiply(data, (float)(object)scalar, result.MutableData.Raw);
            return CastResult(result);
        }

        throw UnsupportedType();
    }

    public T Sum(IHardwareTensor<T> value)
    {
        var tensor = RequireLocal(value);
        if (typeof(T) == typeof(double))
            return (T)(object)TensorPrimitives.Sum(CastSpan<double>(tensor.Data));
        if (typeof(T) == typeof(float))
            return (T)(object)TensorPrimitives.Sum(CastSpan<float>(tensor.Data));

        throw UnsupportedType();
    }

    public T Mean(IHardwareTensor<T> value)
    {
        var tensor = RequireLocal(value);
        if (tensor.Length == 0)
            throw new ArgumentException("Cannot compute mean of an empty tensor.", nameof(value));

        if (typeof(T) == typeof(double))
            return (T)(object)(TensorPrimitives.Sum(CastSpan<double>(tensor.Data)) / tensor.Length);
        if (typeof(T) == typeof(float))
            return (T)(object)(TensorPrimitives.Sum(CastSpan<float>(tensor.Data)) / tensor.Length);

        throw UnsupportedType();
    }

    public T Dot(IHardwareTensor<T> left, IHardwareTensor<T> right)
    {
        var a = RequireLocal(left);
        var b = RequireLocal(right);
        if (a.Length != b.Length)
            throw new ArgumentException("Tensor lengths must match.");

        if (typeof(T) == typeof(double))
            return (T)(object)TensorPrimitives.Dot(CastSpan<double>(a.Data), CastSpan<double>(b.Data));
        if (typeof(T) == typeof(float))
            return (T)(object)TensorPrimitives.Dot(CastSpan<float>(a.Data), CastSpan<float>(b.Data));

        throw UnsupportedType();
    }

    public T Norm(IHardwareTensor<T> value)
    {
        var tensor = RequireLocal(value);
        if (typeof(T) == typeof(double))
            return (T)(object)TensorPrimitives.Norm(CastSpan<double>(tensor.Data));
        if (typeof(T) == typeof(float))
            return (T)(object)TensorPrimitives.Norm(CastSpan<float>(tensor.Data));

        throw UnsupportedType();
    }

    private static IHardwareTensor<T> Elementwise(
        IHardwareTensor<T> left,
        IHardwareTensor<T> right,
        TensorElementwiseOp op)
    {
        var a = RequireLocal(left);
        var b = RequireLocal(right);
        if (a.Rows != b.Rows || a.Cols != b.Cols)
            throw new ArgumentException("Tensor dimensions must match.");

        if (typeof(T) == typeof(double))
        {
            var leftData = CastSpan<double>(a.Data);
            var rightData = CastSpan<double>(b.Data);
            var result = new HardwareTensor<double>(a.Rows, a.Cols);
            var output = result.MutableData.Raw;
            if (op == TensorElementwiseOp.Add)
                TensorPrimitives.Add(leftData, rightData, output);
            else if (op == TensorElementwiseOp.Subtract)
                TensorPrimitives.Subtract(leftData, rightData, output);
            else
                TensorPrimitives.Multiply(leftData, rightData, output);
            return CastResult(result);
        }

        if (typeof(T) == typeof(float))
        {
            var leftData = CastSpan<float>(a.Data);
            var rightData = CastSpan<float>(b.Data);
            var result = new HardwareTensor<float>(a.Rows, a.Cols);
            var output = result.MutableData.Raw;
            if (op == TensorElementwiseOp.Add)
                TensorPrimitives.Add(leftData, rightData, output);
            else if (op == TensorElementwiseOp.Subtract)
                TensorPrimitives.Subtract(leftData, rightData, output);
            else
                TensorPrimitives.Multiply(leftData, rightData, output);
            return CastResult(result);
        }

        throw UnsupportedType();
    }

    private static IHardwareTensor<T> Clone(IHardwareTensor<T> tensor)
    {
        var local = RequireLocal(tensor);
        var data = local.Data.ToArray();
        return new HardwareTensor<T>(local.Rows, local.Cols, data);
    }

    private static HardwareTensor<T> RequireLocal(IHardwareTensor<T> tensor) =>
        tensor as HardwareTensor<T> ?? throw new ArgumentException("Tensor was not created by the local hardware backend.", nameof(tensor));

    private static HardwareTensor<double> MatMulDouble(ReadOnlySpan<double> left, ReadOnlySpan<double> right, int rows, int inner, int cols)
    {
        var result = new double[checked(rows * cols)];
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
            {
                double sum = 0;
                for (var k = 0; k < inner; k++)
                    sum += left[r * inner + k] * right[k * cols + c];
                result[r * cols + c] = sum;
            }
        return new HardwareTensor<double>(rows, cols, result);
    }

    private static HardwareTensor<float> MatMulFloat(ReadOnlySpan<float> left, ReadOnlySpan<float> right, int rows, int inner, int cols)
    {
        var result = new float[checked(rows * cols)];
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
            {
                float sum = 0;
                for (var k = 0; k < inner; k++)
                    sum += left[r * inner + k] * right[k * cols + c];
                result[r * cols + c] = sum;
            }
        return new HardwareTensor<float>(rows, cols, result);
    }

    private static HardwareTensor<double> SolveDouble(
        ReadOnlySpan<double> matrix,
        ReadOnlySpan<double> rightHandSide,
        int n,
        int rhsCols)
    {
        var a = matrix.ToArray();
        var x = rightHandSide.ToArray();

        for (var col = 0; col < n; col++)
        {
            var pivotRow = col;
            var pivotAbs = Math.Abs(a[col * n + col]);
            for (var r = col + 1; r < n; r++)
            {
                var candidate = Math.Abs(a[r * n + col]);
                if (candidate > pivotAbs)
                {
                    pivotAbs = candidate;
                    pivotRow = r;
                }
            }

            if (pivotAbs == 0.0)
                throw new ArithmeticException("Matrix is singular.");

            SwapRows(a, x, n, rhsCols, col, pivotRow);
            var pivot = a[col * n + col];
            ScaleRow(a, x, n, rhsCols, col, 1.0 / pivot);

            for (var r = 0; r < n; r++)
            {
                if (r == col)
                    continue;
                AddScaledRow(a, x, n, rhsCols, targetRow: r, sourceRow: col, scale: -a[r * n + col]);
            }
        }

        return new HardwareTensor<double>(n, rhsCols, x);
    }

    private static HardwareTensor<float> SolveFloat(
        ReadOnlySpan<float> matrix,
        ReadOnlySpan<float> rightHandSide,
        int n,
        int rhsCols)
    {
        var a = matrix.ToArray();
        var x = rightHandSide.ToArray();

        for (var col = 0; col < n; col++)
        {
            var pivotRow = col;
            var pivotAbs = Math.Abs(a[col * n + col]);
            for (var r = col + 1; r < n; r++)
            {
                var candidate = Math.Abs(a[r * n + col]);
                if (candidate > pivotAbs)
                {
                    pivotAbs = candidate;
                    pivotRow = r;
                }
            }

            if (pivotAbs == 0.0f)
                throw new ArithmeticException("Matrix is singular.");

            SwapRows(a, x, n, rhsCols, col, pivotRow);
            var pivot = a[col * n + col];
            ScaleRow(a, x, n, rhsCols, col, 1.0f / pivot);

            for (var r = 0; r < n; r++)
            {
                if (r == col)
                    continue;
                AddScaledRow(a, x, n, rhsCols, targetRow: r, sourceRow: col, scale: -a[r * n + col]);
            }
        }

        return new HardwareTensor<float>(n, rhsCols, x);
    }

    private static void SwapRows(double[] left, double[] right, int leftCols, int rightCols, int rowA, int rowB)
    {
        if (rowA == rowB)
            return;
        for (var c = 0; c < leftCols; c++)
            (left[rowA * leftCols + c], left[rowB * leftCols + c]) = (left[rowB * leftCols + c], left[rowA * leftCols + c]);
        for (var c = 0; c < rightCols; c++)
            (right[rowA * rightCols + c], right[rowB * rightCols + c]) = (right[rowB * rightCols + c], right[rowA * rightCols + c]);
    }

    private static void SwapRows(float[] left, float[] right, int leftCols, int rightCols, int rowA, int rowB)
    {
        if (rowA == rowB)
            return;
        for (var c = 0; c < leftCols; c++)
            (left[rowA * leftCols + c], left[rowB * leftCols + c]) = (left[rowB * leftCols + c], left[rowA * leftCols + c]);
        for (var c = 0; c < rightCols; c++)
            (right[rowA * rightCols + c], right[rowB * rightCols + c]) = (right[rowB * rightCols + c], right[rowA * rightCols + c]);
    }

    private static void ScaleRow(double[] left, double[] right, int leftCols, int rightCols, int row, double scale)
    {
        for (var c = 0; c < leftCols; c++)
            left[row * leftCols + c] *= scale;
        for (var c = 0; c < rightCols; c++)
            right[row * rightCols + c] *= scale;
    }

    private static void ScaleRow(float[] left, float[] right, int leftCols, int rightCols, int row, float scale)
    {
        for (var c = 0; c < leftCols; c++)
            left[row * leftCols + c] *= scale;
        for (var c = 0; c < rightCols; c++)
            right[row * rightCols + c] *= scale;
    }

    private static void AddScaledRow(double[] left, double[] right, int leftCols, int rightCols, int targetRow, int sourceRow, double scale)
    {
        if (scale == 0.0)
            return;
        for (var c = 0; c < leftCols; c++)
            left[targetRow * leftCols + c] += scale * left[sourceRow * leftCols + c];
        for (var c = 0; c < rightCols; c++)
            right[targetRow * rightCols + c] += scale * right[sourceRow * rightCols + c];
    }

    private static void AddScaledRow(float[] left, float[] right, int leftCols, int rightCols, int targetRow, int sourceRow, float scale)
    {
        if (scale == 0.0f)
            return;
        for (var c = 0; c < leftCols; c++)
            left[targetRow * leftCols + c] += scale * left[sourceRow * leftCols + c];
        for (var c = 0; c < rightCols; c++)
            right[targetRow * rightCols + c] += scale * right[sourceRow * rightCols + c];
    }

    private static double[] IdentityDouble(int n)
    {
        var identity = new double[checked(n * n)];
        for (var i = 0; i < n; i++)
        {
            identity[i * n + i] = 1.0;
        }

        return identity;
    }

    private static float[] IdentityFloat(int n)
    {
        var identity = new float[checked(n * n)];
        for (var i = 0; i < n; i++)
        {
            identity[i * n + i] = 1.0f;
        }

        return identity;
    }

    private static ReadOnlySpan<TTarget> CastSpan<TTarget>(ReadOnlySpan<T> source)
        where TTarget : unmanaged =>
        System.Runtime.InteropServices.MemoryMarshal.Cast<T, TTarget>(source);

    private static IHardwareTensor<T> CastResult<TSource>(HardwareTensor<TSource> tensor)
        where TSource : unmanaged =>
        (IHardwareTensor<T>)(object)tensor;

    private static NotSupportedException UnsupportedType() =>
        new("LocalHardwareBackend only supports double and float tensors.");

    private enum TensorElementwiseOp
    {
        Add,
        Subtract,
        Multiply
    }
}
