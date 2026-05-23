using Helium.Validated;

namespace Helium.Hardware;

/// <summary>
/// Explicit conversions from hardware numeric results into validated enclosures.
/// The caller supplies the uncertainty radius; hardware values alone do not
/// carry a correctness proof.
/// </summary>
public static class ValidatedConvert
{
    public static IntervalMatrix ToIntervals(DoubleMatrix source, double radius)
    {
        ValidateRadius(radius);
        var data = new Interval[source.Length];
        var sourceData = source.Data;
        for (int i = 0; i < sourceData.Length; i++)
            data[i] = new Interval(sourceData[i] - radius, sourceData[i] + radius);
        return new IntervalMatrix(source.Rows, source.Cols, data);
    }

    public static IntervalMatrix ToIntervals(FloatMatrix source, double radius)
    {
        ValidateRadius(radius);
        var data = new Interval[source.Length];
        var sourceData = source.Data;
        for (int i = 0; i < sourceData.Length; i++)
            data[i] = new Interval(sourceData[i] - radius, sourceData[i] + radius);
        return new IntervalMatrix(source.Rows, source.Cols, data);
    }

    private static void ValidateRadius(double radius)
    {
        if (radius < 0.0 || double.IsNaN(radius))
            throw new ArgumentOutOfRangeException(nameof(radius), "Radius must be non-negative.");
    }
}
