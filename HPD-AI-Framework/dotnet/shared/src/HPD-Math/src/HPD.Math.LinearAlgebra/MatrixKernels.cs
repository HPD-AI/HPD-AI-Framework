using HPD.Math.Core;

namespace HPD.Math.LinearAlgebra;

/// <summary>
/// Allocation-free kernels over dense row-major matrix views.
/// </summary>
public static class MatrixKernels
{
    public static AlgebraStatus TryAdd<T, TOps>(
        MatrixView<T> left,
        MatrixView<T> right,
        ref MatrixBuilder<T> destination,
        TOps ops)
        where TOps : struct, IAdditiveCommutativeMonoidOps<T>
    {
        var status = ValidateSameShape(left, right);
        if (status != AlgebraStatus.Ok)
            return status;

        status = destination.TrySetShape(left.Rows, left.Columns);
        if (status != AlgebraStatus.Ok)
            return status;

        var output = destination.WrittenSpan;
        for (var i = 0; i < left.Count; i++)
            ops.Add(ref output[i], left.Values[i], right.Values[i]);

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TrySub<T, TOps>(
        MatrixView<T> left,
        MatrixView<T> right,
        ref MatrixBuilder<T> destination,
        TOps ops)
        where TOps : struct, IRingOps<T>
    {
        var status = ValidateSameShape(left, right);
        if (status != AlgebraStatus.Ok)
            return status;

        status = destination.TrySetShape(left.Rows, left.Columns);
        if (status != AlgebraStatus.Ok)
            return status;

        var output = destination.WrittenSpan;
        for (var i = 0; i < left.Count; i++)
            ops.Sub(ref output[i], left.Values[i], right.Values[i]);

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryScale<T, TOps>(
        MatrixView<T> value,
        in T scalar,
        ref MatrixBuilder<T> destination,
        TOps ops)
        where TOps : struct, IRingOps<T>
    {
        var status = value.ValidateShape();
        if (status != AlgebraStatus.Ok)
            return status;

        status = destination.TrySetShape(value.Rows, value.Columns);
        if (status != AlgebraStatus.Ok)
            return status;

        var output = destination.WrittenSpan;
        for (var i = 0; i < value.Count; i++)
            ops.Mul(ref output[i], scalar, value.Values[i]);

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryTranspose<T>(
        MatrixView<T> value,
        ref MatrixBuilder<T> destination)
    {
        var status = value.ValidateShape();
        if (status != AlgebraStatus.Ok)
            return status;

        status = destination.TrySetShape(value.Columns, value.Rows);
        if (status != AlgebraStatus.Ok)
            return status;

        var output = destination.WrittenSpan;
        for (var row = 0; row < value.Rows; row++)
        {
            for (var column = 0; column < value.Columns; column++)
                output[(column * value.Rows) + row] = value[row, column];
        }

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryMul<T, TOps>(
        MatrixView<T> left,
        MatrixView<T> right,
        ref MatrixBuilder<T> destination,
        TOps ops)
        where TOps : struct, IRingOps<T>
    {
        var status = left.ValidateShape();
        if (status != AlgebraStatus.Ok)
            return status;

        status = right.ValidateShape();
        if (status != AlgebraStatus.Ok)
            return status;

        if (left.Columns != right.Rows)
            return AlgebraStatus.DimensionMismatch;

        status = destination.TrySetShape(left.Rows, right.Columns);
        if (status != AlgebraStatus.Ok)
            return status;

        var output = destination.WrittenSpan;
        for (var row = 0; row < left.Rows; row++)
        {
            for (var column = 0; column < right.Columns; column++)
            {
                var sum = ops.Zero;
                for (var k = 0; k < left.Columns; k++)
                {
                    var product = ops.Zero;
                    ops.Mul(ref product, left[row, k], right[k, column]);

                    var next = ops.Zero;
                    ops.Add(ref next, sum, product);
                    sum = next;
                }

                output[(row * right.Columns) + column] = sum;
            }
        }

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryMulVector<T, TOps>(
        MatrixView<T> matrix,
        VectorView<T> vector,
        ref VectorBuilder<T> destination,
        TOps ops)
        where TOps : struct, IRingOps<T>
    {
        var status = matrix.ValidateShape();
        if (status != AlgebraStatus.Ok)
            return status;

        if (matrix.Columns != vector.Length)
            return AlgebraStatus.DimensionMismatch;

        status = destination.TrySetLength(matrix.Rows);
        if (status != AlgebraStatus.Ok)
            return status;

        var output = destination.WrittenSpan;
        for (var row = 0; row < matrix.Rows; row++)
        {
            var sum = ops.Zero;
            for (var column = 0; column < matrix.Columns; column++)
            {
                var product = ops.Zero;
                ops.Mul(ref product, matrix[row, column], vector[column]);

                var next = ops.Zero;
                ops.Add(ref next, sum, product);
                sum = next;
            }

            output[row] = sum;
        }

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryIdentity<T, TOps>(
        int size,
        ref MatrixBuilder<T> destination,
        TOps ops)
        where TOps : struct, IRingOps<T>
    {
        var status = destination.TrySetShape(size, size);
        if (status != AlgebraStatus.Ok)
            return status;

        var output = destination.WrittenSpan;
        output.Fill(ops.Zero);
        for (var i = 0; i < size; i++)
            output[(i * size) + i] = ops.One;

        return AlgebraStatus.Ok;
    }

    private static AlgebraStatus ValidateSameShape<T>(MatrixView<T> left, MatrixView<T> right)
    {
        var status = left.ValidateShape();
        if (status != AlgebraStatus.Ok)
            return status;

        status = right.ValidateShape();
        if (status != AlgebraStatus.Ok)
            return status;

        return left.Rows == right.Rows && left.Columns == right.Columns
            ? AlgebraStatus.Ok
            : AlgebraStatus.DimensionMismatch;
    }
}
