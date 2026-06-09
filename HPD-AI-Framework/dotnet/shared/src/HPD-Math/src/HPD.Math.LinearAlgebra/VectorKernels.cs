using HPD.Math.Core;

namespace HPD.Math.LinearAlgebra;

/// <summary>
/// Allocation-free kernels over dense vector views.
/// </summary>
public static class VectorKernels
{
    public static AlgebraStatus TryAdd<T, TOps>(
        VectorView<T> left,
        VectorView<T> right,
        ref VectorBuilder<T> destination,
        TOps ops)
        where TOps : struct, IAdditiveCommutativeMonoidOps<T>
    {
        if (left.Length != right.Length)
            return AlgebraStatus.DimensionMismatch;
        if (destination.Capacity < left.Length)
            return AlgebraStatus.InsufficientDestination;

        var status = destination.TrySetLength(left.Length);
        if (status != AlgebraStatus.Ok)
            return status;

        var output = destination.WrittenSpan;
        for (var i = 0; i < left.Length; i++)
            ops.Add(ref output[i], left[i], right[i]);

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TrySub<T, TOps>(
        VectorView<T> left,
        VectorView<T> right,
        ref VectorBuilder<T> destination,
        TOps ops)
        where TOps : struct, IRingOps<T>
    {
        if (left.Length != right.Length)
            return AlgebraStatus.DimensionMismatch;
        if (destination.Capacity < left.Length)
            return AlgebraStatus.InsufficientDestination;

        var status = destination.TrySetLength(left.Length);
        if (status != AlgebraStatus.Ok)
            return status;

        var output = destination.WrittenSpan;
        for (var i = 0; i < left.Length; i++)
            ops.Sub(ref output[i], left[i], right[i]);

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryScale<T, TOps>(
        VectorView<T> value,
        in T scalar,
        ref VectorBuilder<T> destination,
        TOps ops)
        where TOps : struct, IRingOps<T>
    {
        if (destination.Capacity < value.Length)
            return AlgebraStatus.InsufficientDestination;

        var status = destination.TrySetLength(value.Length);
        if (status != AlgebraStatus.Ok)
            return status;

        var output = destination.WrittenSpan;
        for (var i = 0; i < value.Length; i++)
            ops.Mul(ref output[i], scalar, value[i]);

        return AlgebraStatus.Ok;
    }

    public static AlgebraStatus TryDot<T, TOps>(
        VectorView<T> left,
        VectorView<T> right,
        ref T destination,
        TOps ops)
        where TOps : struct, IRingOps<T>
    {
        if (left.Length != right.Length)
            return AlgebraStatus.DimensionMismatch;

        destination = ops.Zero;
        for (var i = 0; i < left.Length; i++)
        {
            var product = ops.Zero;
            ops.Mul(ref product, left[i], right[i]);
            var sum = ops.Zero;
            ops.Add(ref sum, destination, product);
            destination = sum;
        }

        return AlgebraStatus.Ok;
    }
}
