using System.Numerics;
using Helium.Algebra;
using Helium.Primitives;

namespace Helium.Hardware;

/// <summary>
/// Explicit lossy conversions from exact host algebra into hardware containers.
/// </summary>
public static class HardwareConvert
{
    public static DoubleMatrix ApproximateToFloat64(Matrix<Rational> source)
    {
        var data = ApproximateToFloat64Data(source);
        return new DoubleMatrix(source.Rows, source.Cols, data);
    }

    public static FloatMatrix ApproximateToFloat32(Matrix<Rational> source)
    {
        var data = ApproximateToFloat32Data(source);
        return new FloatMatrix(source.Rows, source.Cols, data);
    }

    public static DoubleMatrix ApproximateToFloat64(Matrix<Integer> source)
    {
        var data = ApproximateToFloat64Data(source);
        return new DoubleMatrix(source.Rows, source.Cols, data);
    }

    public static FloatMatrix ApproximateToFloat32(Matrix<Integer> source)
    {
        var data = ApproximateToFloat32Data(source);
        return new FloatMatrix(source.Rows, source.Cols, data);
    }

    public static IHardwareTensor<double> ApproximateToFloat64Tensor(
        IExecutionBackend<double> backend,
        Matrix<Rational> source)
    {
        ArgumentNullException.ThrowIfNull(backend);
        return backend.CreateMatrix(source.Rows, source.Cols, ApproximateToFloat64Data(source));
    }

    public static IHardwareTensor<float> ApproximateToFloat32Tensor(
        IExecutionBackend<float> backend,
        Matrix<Rational> source)
    {
        ArgumentNullException.ThrowIfNull(backend);
        return backend.CreateMatrix(source.Rows, source.Cols, ApproximateToFloat32Data(source));
    }

    public static IHardwareTensor<double> ApproximateToFloat64Tensor(
        IExecutionBackend<double> backend,
        Matrix<Integer> source)
    {
        ArgumentNullException.ThrowIfNull(backend);
        return backend.CreateMatrix(source.Rows, source.Cols, ApproximateToFloat64Data(source));
    }

    public static IHardwareTensor<float> ApproximateToFloat32Tensor(
        IExecutionBackend<float> backend,
        Matrix<Integer> source)
    {
        ArgumentNullException.ThrowIfNull(backend);
        return backend.CreateMatrix(source.Rows, source.Cols, ApproximateToFloat32Data(source));
    }

    public static HardwareBuffer<double> ApproximateToFloat64CoefficientBuffer(DensePolynomial<Rational> source)
    {
        var coefficients = source.Coefficients;
        var data = new double[coefficients.Length];
        for (int i = 0; i < coefficients.Length; i++)
            data[i] = ToDouble(coefficients[i]);
        return new HardwareBuffer<double>(data);
    }

    public static HardwareBuffer<float> ApproximateToFloat32CoefficientBuffer(DensePolynomial<Rational> source)
    {
        var coefficients = source.Coefficients;
        var data = new float[coefficients.Length];
        for (int i = 0; i < coefficients.Length; i++)
            data[i] = (float)ToDouble(coefficients[i]);
        return new HardwareBuffer<float>(data);
    }

    public static HardwareBuffer<double> ApproximateToFloat64CoefficientBuffer(DensePolynomial<Integer> source)
    {
        var coefficients = source.Coefficients;
        var data = new double[coefficients.Length];
        for (int i = 0; i < coefficients.Length; i++)
            data[i] = ToDouble(coefficients[i]);
        return new HardwareBuffer<double>(data);
    }

    public static HardwareBuffer<float> ApproximateToFloat32CoefficientBuffer(DensePolynomial<Integer> source)
    {
        var coefficients = source.Coefficients;
        var data = new float[coefficients.Length];
        for (int i = 0; i < coefficients.Length; i++)
            data[i] = (float)ToDouble(coefficients[i]);
        return new HardwareBuffer<float>(data);
    }

    public static DoubleMatrix ToDoubleMatrix(Matrix<Rational> source) => ApproximateToFloat64(source);

    public static FloatMatrix ToFloatMatrix(Matrix<Rational> source) => ApproximateToFloat32(source);

    public static DoubleMatrix ToDoubleMatrix(Matrix<Integer> source) => ApproximateToFloat64(source);

    public static FloatMatrix ToFloatMatrix(Matrix<Integer> source) => ApproximateToFloat32(source);

    public static HardwareBuffer<double> ToDoubleCoefficientBuffer(DensePolynomial<Rational> source) =>
        ApproximateToFloat64CoefficientBuffer(source);

    public static HardwareBuffer<float> ToFloatCoefficientBuffer(DensePolynomial<Rational> source) =>
        ApproximateToFloat32CoefficientBuffer(source);

    public static HardwareBuffer<double> ToDoubleCoefficientBuffer(DensePolynomial<Integer> source) =>
        ApproximateToFloat64CoefficientBuffer(source);

    public static HardwareBuffer<float> ToFloatCoefficientBuffer(DensePolynomial<Integer> source) =>
        ApproximateToFloat32CoefficientBuffer(source);

    private static double[] ApproximateToFloat64Data(Matrix<Rational> source)
    {
        var data = new double[source.Rows * source.Cols];
        for (int row = 0; row < source.Rows; row++)
        for (int col = 0; col < source.Cols; col++)
            data[row * source.Cols + col] = ToDouble(source[row, col]);
        return data;
    }

    private static float[] ApproximateToFloat32Data(Matrix<Rational> source)
    {
        var data = new float[source.Rows * source.Cols];
        for (int row = 0; row < source.Rows; row++)
        for (int col = 0; col < source.Cols; col++)
            data[row * source.Cols + col] = (float)ToDouble(source[row, col]);
        return data;
    }

    private static double[] ApproximateToFloat64Data(Matrix<Integer> source)
    {
        var data = new double[source.Rows * source.Cols];
        for (int row = 0; row < source.Rows; row++)
        for (int col = 0; col < source.Cols; col++)
            data[row * source.Cols + col] = ToDouble(source[row, col]);
        return data;
    }

    private static float[] ApproximateToFloat32Data(Matrix<Integer> source)
    {
        var data = new float[source.Rows * source.Cols];
        for (int row = 0; row < source.Rows; row++)
        for (int col = 0; col < source.Cols; col++)
            data[row * source.Cols + col] = (float)ToDouble(source[row, col]);
        return data;
    }

    private static double ToDouble(Rational value) =>
        ToDouble(value.Numerator) / ToDouble(value.Denominator);

    private static double ToDouble(Integer value) =>
        (double)(BigInteger)value;
}
