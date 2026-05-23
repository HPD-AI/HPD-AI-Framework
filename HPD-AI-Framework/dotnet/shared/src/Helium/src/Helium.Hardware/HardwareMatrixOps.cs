using System.Numerics.Tensors;

namespace Helium.Hardware;

/// <summary>
/// Explicit CPU hardware operations over contiguous matrix buffers.
/// Matrix multiplication stays in <see cref="Blas"/>; this type is for elementwise work and reductions.
/// </summary>
public static class HardwareMatrixOps
{
    public static DoubleMatrix Add(DoubleMatrix left, DoubleMatrix right)
    {
        RequireSameShape(left, right);
        var result = new DoubleMatrix(left.Rows, left.Cols);
        TensorPrimitives.Add(left.Data, right.Data, result.Buffer.AsSpan().Raw);
        return result;
    }

    public static FloatMatrix Add(FloatMatrix left, FloatMatrix right)
    {
        RequireSameShape(left, right);
        var result = new FloatMatrix(left.Rows, left.Cols);
        TensorPrimitives.Add(left.Data, right.Data, result.Buffer.AsSpan().Raw);
        return result;
    }

    public static DoubleMatrix Subtract(DoubleMatrix left, DoubleMatrix right)
    {
        RequireSameShape(left, right);
        var result = new DoubleMatrix(left.Rows, left.Cols);
        TensorPrimitives.Subtract(left.Data, right.Data, result.Buffer.AsSpan().Raw);
        return result;
    }

    public static FloatMatrix Subtract(FloatMatrix left, FloatMatrix right)
    {
        RequireSameShape(left, right);
        var result = new FloatMatrix(left.Rows, left.Cols);
        TensorPrimitives.Subtract(left.Data, right.Data, result.Buffer.AsSpan().Raw);
        return result;
    }

    public static DoubleMatrix Multiply(DoubleMatrix left, DoubleMatrix right)
    {
        RequireSameShape(left, right);
        var result = new DoubleMatrix(left.Rows, left.Cols);
        TensorPrimitives.Multiply(left.Data, right.Data, result.Buffer.AsSpan().Raw);
        return result;
    }

    public static FloatMatrix Multiply(FloatMatrix left, FloatMatrix right)
    {
        RequireSameShape(left, right);
        var result = new FloatMatrix(left.Rows, left.Cols);
        TensorPrimitives.Multiply(left.Data, right.Data, result.Buffer.AsSpan().Raw);
        return result;
    }

    public static DoubleMatrix Negate(DoubleMatrix value)
    {
        var result = new DoubleMatrix(value.Rows, value.Cols);
        TensorPrimitives.Negate(value.Data, result.Buffer.AsSpan().Raw);
        return result;
    }

    public static FloatMatrix Negate(FloatMatrix value)
    {
        var result = new FloatMatrix(value.Rows, value.Cols);
        TensorPrimitives.Negate(value.Data, result.Buffer.AsSpan().Raw);
        return result;
    }

    public static DoubleMatrix Scale(DoubleMatrix value, double scalar)
    {
        var result = new DoubleMatrix(value.Rows, value.Cols);
        TensorPrimitives.Multiply(value.Data, scalar, result.Buffer.AsSpan().Raw);
        return result;
    }

    public static FloatMatrix Scale(FloatMatrix value, float scalar)
    {
        var result = new FloatMatrix(value.Rows, value.Cols);
        TensorPrimitives.Multiply(value.Data, scalar, result.Buffer.AsSpan().Raw);
        return result;
    }

    public static double Sum(DoubleMatrix value) => TensorPrimitives.Sum(value.Data);

    public static float Sum(FloatMatrix value) => TensorPrimitives.Sum(value.Data);

    public static double Mean(DoubleMatrix value) => value.Length == 0
        ? throw new ArgumentException("Cannot compute mean of an empty matrix.", nameof(value))
        : Sum(value) / value.Length;

    public static float Mean(FloatMatrix value) => value.Length == 0
        ? throw new ArgumentException("Cannot compute mean of an empty matrix.", nameof(value))
        : Sum(value) / value.Length;

    public static double Dot(DoubleMatrix left, DoubleMatrix right)
    {
        RequireSameLength(left, right);
        return TensorPrimitives.Dot(left.Data, right.Data);
    }

    public static float Dot(FloatMatrix left, FloatMatrix right)
    {
        RequireSameLength(left, right);
        return TensorPrimitives.Dot(left.Data, right.Data);
    }

    public static double Norm(DoubleMatrix value) => TensorPrimitives.Norm(value.Data);

    public static float Norm(FloatMatrix value) => TensorPrimitives.Norm(value.Data);

    private static void RequireSameShape(DoubleMatrix left, DoubleMatrix right)
    {
        if (left.Rows != right.Rows || left.Cols != right.Cols)
            throw new ArgumentException("Matrix dimensions must match.");
    }

    private static void RequireSameShape(FloatMatrix left, FloatMatrix right)
    {
        if (left.Rows != right.Rows || left.Cols != right.Cols)
            throw new ArgumentException("Matrix dimensions must match.");
    }

    private static void RequireSameLength(DoubleMatrix left, DoubleMatrix right)
    {
        if (left.Length != right.Length)
            throw new ArgumentException("Matrix lengths must match.");
    }

    private static void RequireSameLength(FloatMatrix left, FloatMatrix right)
    {
        if (left.Length != right.Length)
            throw new ArgumentException("Matrix lengths must match.");
    }
}
